using System.Net;
using System.Net.Http.Json;
using Asp.Versioning;
using Asp.Versioning.Http;
using eShop.BusinessRules.Client.Pricing;
using eShop.BusinessRules.Client.Snapshot;

namespace eShop.Ordering.FunctionalTests;

/// <summary>Spec acceptance criterion "BusinessRules.API is down": checkout still works at catalog prices.</summary>
public sealed class ResilienceTests : IClassFixture<OrderingApiFixture>
{
    private readonly OrderingApiFixture _fixture;
    private readonly HttpClient _httpClientV1;
    private readonly TestBuyerOrders _testBuyerOrders;

    public ResilienceTests(OrderingApiFixture fixture)
    {
        _fixture = fixture;
        _httpClientV1 = fixture.CreateDefaultClient(new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(1.0)));
        _testBuyerOrders = new TestBuyerOrders(fixture);
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

            var createResponse = await _testBuyerOrders.PostV2Async(Guid.NewGuid(), items, quote.Total);

            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            var order = await _testBuyerOrders.GetSingleAsync();
            Assert.Equal(43.97m, order.Total);
            Assert.Equal(0m, order.ShippingFee);
            Assert.Collection(order.OrderItems.OrderBy(i => i.UnitPrice),
                i => { Assert.Equal(0.99, i.UnitPrice); Assert.Equal(3, i.Units); },
                i => { Assert.Equal(20.50, i.UnitPrice); Assert.Equal(2, i.Units); });
        }
        finally
        {
            await _testBuyerOrders.DeleteAllAsync();
        }
    }
}
