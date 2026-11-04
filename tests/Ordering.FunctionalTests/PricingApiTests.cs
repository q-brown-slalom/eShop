using System.Net;
using System.Net.Http.Json;
using Asp.Versioning;
using Asp.Versioning.Http;
using eShop.BusinessRules.Client.Pricing;
using eShop.BusinessRules.Client.Snapshot;

namespace eShop.Ordering.FunctionalTests;

public sealed class PricingApiTests : IClassFixture<OrderingApiFixture>
{
    private const string QuoteUrl = "api/pricing/quote";

    private readonly OrderingApiFixture _fixture;
    private readonly HttpClient _httpClient;

    public PricingApiTests(OrderingApiFixture fixture)
    {
        var handler = new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(1.0));

        _fixture = fixture;
        _httpClient = fixture.CreateDefaultClient(handler);
    }

    [Fact]
    public async Task QuoteReturnsCatalogPricesWithDefaults()
    {
        var response = await PostQuoteAsync([new QuoteLine(1, 2)]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await ReadQuoteAsync(response);
        var line = Assert.Single(quote.Lines);
        Assert.Equal(1, line.ProductId);
        Assert.Equal(2, line.Units);
        Assert.Equal(10.00m, line.BasePrice);
        Assert.Equal(10.00m, line.UnitPrice);
        Assert.Equal(0m, line.Discount);
        Assert.Equal(20.00m, quote.Subtotal);
        Assert.Equal(0m, quote.DiscountTotal);
        Assert.Equal(0m, quote.ShippingFee);
        Assert.Equal(20.00m, quote.Total);
        Assert.Equal(0, quote.SnapshotVersion);
    }

    [Fact]
    public async Task QuoteAllowsAnonymousCaller()
    {
        var response = await PostQuoteAsync([new QuoteLine(2, 1)], anonymous: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await ReadQuoteAsync(response);
        Assert.Equal(20.50m, quote.Total);
    }

    [Fact]
    public async Task QuoteWithUnknownProductReturnsBadRequest()
    {
        var response = await PostQuoteAsync([new QuoteLine(1, 1), new QuoteLine(777, 1), new QuoteLine(888, 2)]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadValidationProblemAsync(response);
        var messages = Assert.Contains("items", problem.Errors);
        var message = Assert.Single(messages);
        Assert.Contains("777", message);
        Assert.Contains("888", message);
        Assert.DoesNotContain("1,", message);
    }

    [Fact]
    public async Task QuoteWithMoreThan100LinesReturnsBadRequest()
    {
        var lines = Enumerable.Range(0, 101).Select(_ => new QuoteLine(1, 1)).ToList();

        var response = await PostQuoteAsync(lines);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadValidationProblemAsync(response);
        Assert.Contains("items", problem.Errors);
    }

    [Fact]
    public async Task QuoteWithNoLinesReturnsBadRequest()
    {
        var response = await PostQuoteAsync([]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadValidationProblemAsync(response);
        Assert.Contains("items", problem.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public async Task QuoteWithNullBodyReturnsBadRequest(string body)
    {
        var response = await PostRawQuoteAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadValidationProblemAsync(response);
        Assert.Contains("items", problem.Errors);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"items":null}""")]
    public async Task QuoteWithNullItemsReturnsBadRequest(string body)
    {
        var response = await PostRawQuoteAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadValidationProblemAsync(response);
        Assert.Contains("items", problem.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    [InlineData(int.MaxValue)]
    public async Task QuoteWithUnitsOutOfRangeReturnsBadRequest(int units)
    {
        var response = await PostQuoteAsync([new QuoteLine(1, 1), new QuoteLine(2, units)]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadValidationProblemAsync(response);
        Assert.Contains("items[1].units", problem.Errors);
    }

    [Fact]
    public async Task QuoteReturnsServiceUnavailableWhenCatalogUnavailable()
    {
        var response = await PostQuoteAsync([new QuoteLine(FakeCatalogPriceSource.UnavailableProductId, 1)]);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task QuoteSucceedsWithDefaultsWhenSnapshotSourceUnavailable()
    {
        // The host's snapshot source throws on every call (BusinessRules.API down or never published).
        await _fixture.RulesSnapshotSource.FirstCallAttempted.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.True(_fixture.RulesSnapshotSource.Calls > 0);
        Assert.Equal(0, _fixture.Services.GetRequiredService<RulesSnapshotCache>().Current.Version);

        var response = await PostQuoteAsync([new QuoteLine(3, 2), new QuoteLine(4, 1), new QuoteLine(3, 1)]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await ReadQuoteAsync(response);
        Assert.Equal(0, quote.SnapshotVersion);
        Assert.Collection(quote.Lines,
            l => { Assert.Equal(3, l.ProductId); Assert.Equal(3, l.Units); Assert.Equal(5.25m, l.UnitPrice); },
            l => { Assert.Equal(4, l.ProductId); Assert.Equal(1, l.Units); Assert.Equal(99.99m, l.UnitPrice); });
        Assert.Equal(115.74m, quote.Total);
    }

    private Task<HttpResponseMessage> PostRawQuoteAsync(string json)
    {
        var content = new StringContent(json);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return _httpClient.PostAsync(QuoteUrl, content, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> PostQuoteAsync(List<QuoteLine> items, bool anonymous = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, QuoteUrl)
        {
            Content = JsonContent.Create(new { items })
        };
        if (anonymous)
        {
            request.Headers.Add(AutoAuthorizeMiddleware.AnonymousHeader, "true");
        }

        return await _httpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<PriceQuote> ReadQuoteAsync(HttpResponseMessage response)
    {
        var quote = await response.Content.ReadFromJsonAsync<PriceQuote>(TestContext.Current.CancellationToken);
        Assert.NotNull(quote);
        return quote;
    }

    private static async Task<HttpValidationProblemDetails> ReadValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(TestContext.Current.CancellationToken);
        Assert.NotNull(problem);
        return problem;
    }
}
