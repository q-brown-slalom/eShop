using System.Net;
using System.Text.Json;
using eShop.BusinessRules.Client.Pricing;

namespace eShop.WebApp.Services;

public class OrderingService(HttpClient httpClient, IConfiguration configuration)
{
    public const string PricesChangedProblemType = "https://eshop/problems/prices-changed";

    private readonly string remoteServiceBaseUrl = "/api/Orders/";

    public Task<OrderRecord[]> GetOrders()
    {
        return httpClient.GetFromJsonAsync<OrderRecord[]>(remoteServiceBaseUrl)!;
    }

    /// <summary>
    /// Creates the order with create order v2. Throws <see cref="PricesChangedException"/> with the fresh quote
    /// when the server total no longer matches <see cref="CreateOrderV2Request.ExpectedTotal"/>.
    /// </summary>
    public async Task CreateOrder(CreateOrderV2Request request, Guid requestId)
    {
        // Rollback path: Ordering:UseCreateOrderV1 sends the v1 body, which the server prices without a total check.
        var useV1 = configuration.GetValue("Ordering:UseCreateOrderV1", false);

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, useV1 ? $"{remoteServiceBaseUrl}?api-version=1.0" : remoteServiceBaseUrl);
        requestMessage.Headers.Add("x-requestid", requestId.ToString());
        requestMessage.Content = useV1 ? JsonContent.Create(ToV1(request)) : JsonContent.Create(request);

        using var response = await httpClient.SendAsync(requestMessage);

        if (response.StatusCode == HttpStatusCode.Conflict && await ReadPricesChangedQuoteAsync(response) is { } quote)
        {
            throw new PricesChangedException(quote);
        }

        response.EnsureSuccessStatusCode();
    }

    private static async Task<PriceQuote?> ReadPricesChangedQuoteAsync(HttpResponseMessage response)
    {
        try
        {
            using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (problem.RootElement.ValueKind == JsonValueKind.Object
                && problem.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == PricesChangedProblemType
                && problem.RootElement.TryGetProperty("quote", out var quote))
            {
                return quote.Deserialize<PriceQuote>(JsonSerializerOptions.Web);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static CreateOrderRequest ToV1(CreateOrderV2Request request) => new(
        request.UserId,
        request.UserName,
        request.City,
        request.Street,
        request.State,
        request.Country,
        request.ZipCode,
        request.CardNumber,
        request.CardHolderName,
        request.CardExpiration,
        request.CardSecurityNumber,
        request.CardTypeId,
        request.Buyer,
        // v1 ignores client prices and names: the server prices the order from the catalog.
        [.. request.Items.Select(line => new BasketItem
        {
            Id = line.ProductId.ToString(),
            ProductId = line.ProductId,
            ProductName = string.Empty,
            Quantity = line.Units,
        })]);
}

public record OrderRecord(
    int OrderNumber,
    DateTime Date,
    string Status,
    decimal Total);

public record CreateOrderV2Request(
    string UserId,
    string UserName,
    string City,
    string Street,
    string State,
    string Country,
    string ZipCode,
    string CardNumber,
    string CardHolderName,
    DateTime CardExpiration,
    string CardSecurityNumber,
    int CardTypeId,
    string Buyer,
    List<QuoteLine> Items,
    decimal ExpectedTotal);
