using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Asp.Versioning;
using Asp.Versioning.Http;
using eShop.BusinessRules.Client.Pricing;
using eShop.BusinessRules.Client.Snapshot;
using eShop.Ordering.API.Application.Models;
using eShop.Ordering.API.Application.Queries;
using eShop.Ordering.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace eShop.Ordering.FunctionalTests;

/// <summary>Spec acceptance criterion "BusinessRules.API is down": checkout still works at catalog prices.</summary>
public sealed class ResilienceTests : IClassFixture<OrderingApiFixture>
{
    private readonly OrderingApiFixture _fixture;
    private readonly HttpClient _httpClientV1;
    private readonly HttpClient _httpClientV2;

    public ResilienceTests(OrderingApiFixture fixture)
    {
        _fixture = fixture;
        _httpClientV1 = fixture.CreateDefaultClient(new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(1.0)));
        _httpClientV2 = fixture.CreateDefaultClient(new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(2.0)));
    }

    [Fact]
    public async Task CheckoutSucceedsWhenRulesServiceUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            // The fixture's snapshot source throws on every fetch, so the cache never leaves the built-in defaults.
            await _fixture.RulesSnapshotSource.FirstCallAttempted.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            Assert.Equal(0, _fixture.Services.GetRequiredService<RulesSnapshotCache>().Current.Version);

            List<QuoteLine> items = [new QuoteLine(2, 2), new QuoteLine(5, 3)];

            var quoteResponse = await _httpClientV1.PostAsJsonAsync("api/pricing/quote", new { items }, cancellationToken);

            Assert.Equal(HttpStatusCode.OK, quoteResponse.StatusCode);
            var quote = await quoteResponse.Content.ReadFromJsonAsync<PriceQuote>(cancellationToken);
            Assert.NotNull(quote);
            Assert.Equal(0, quote.SnapshotVersion);
            Assert.Equal(43.97m, quote.Total);

            var createResponse = await PostOrderV2Async(items, quote.Total);

            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            var order = await GetSingleTestBuyerOrderAsync();
            Assert.Equal(43.97m, order.Total);
            Assert.Equal(0m, order.ShippingFee);
            Assert.Collection(order.OrderItems.OrderBy(i => i.UnitPrice),
                i => { Assert.Equal(0.99, i.UnitPrice); Assert.Equal(3, i.Units); },
                i => { Assert.Equal(20.50, i.UnitPrice); Assert.Equal(2, i.Units); });
        }
        finally
        {
            await DeleteTestBuyerOrdersAsync();
        }
    }

    private async Task<HttpResponseMessage> PostOrderV2Async(List<QuoteLine> items, decimal expectedTotal)
    {
        var body = new CreateOrderV2Request(AutoAuthorizeMiddleware.IDENTITY_ID, "TestUser", "city", "street", "state", "country", "12345",
            "4012888888881881", "Test User", DateTime.UtcNow.AddYears(1), "123", 1, "test buyer", items, expectedTotal);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/orders") { Content = JsonContent.Create(body) };
        request.Headers.Add("x-requestid", Guid.NewGuid().ToString());
        return await _httpClientV2.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<Order> GetSingleTestBuyerOrderAsync()
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var orders = await _httpClientV1.GetFromJsonAsync<List<OrderSummary>>("api/orders", jsonOptions, TestContext.Current.CancellationToken);
        var summary = Assert.Single(orders);
        return await _httpClientV1.GetFromJsonAsync<Order>($"api/orders/{summary.OrderNumber}", jsonOptions, TestContext.Current.CancellationToken);
    }

    private async Task DeleteTestBuyerOrdersAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
        var identity = AutoAuthorizeMiddleware.IDENTITY_ID;
        await context.Database.ExecuteSqlAsync(
            $"""DELETE FROM ordering.orders WHERE "BuyerId" IN (SELECT "Id" FROM ordering.buyers WHERE "IdentityGuid" = {identity})""",
            cancellationToken);
        await context.Database.ExecuteSqlAsync(
            $"""DELETE FROM ordering.paymentmethods WHERE "BuyerId" IN (SELECT "Id" FROM ordering.buyers WHERE "IdentityGuid" = {identity})""",
            cancellationToken);
        await context.Database.ExecuteSqlAsync(
            $"""DELETE FROM ordering.buyers WHERE "IdentityGuid" = {identity}""",
            cancellationToken);
    }
}
