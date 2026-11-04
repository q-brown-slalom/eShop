using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Asp.Versioning;
using Asp.Versioning.Http;
using eShop.BusinessRules.Client.Pricing;
using eShop.BusinessRules.Client.Rules;
using eShop.BusinessRules.Client.Rules.Types;
using eShop.BusinessRules.Client.Snapshot;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;
using eShop.Ordering.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace eShop.Ordering.FunctionalTests;

/// <summary>Published rule types are quoted, charged exactly, and stored on the order at creation.</summary>
public sealed class RuleTypesOrderingTests : IClassFixture<OrderingApiFixture>
{
    private readonly OrderingApiFixture _fixture;
    private readonly HttpClient _httpClientV1;
    private readonly TestBuyerOrders _testBuyerOrders;

    public RuleTypesOrderingTests(OrderingApiFixture fixture)
    {
        _fixture = fixture;
        _httpClientV1 = fixture.CreateDefaultClient(new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(1.0)));
        _testBuyerOrders = new TestBuyerOrders(fixture);
    }

    [Fact]
    public async Task TypeTenPercentOffIsQuotedAndChargedExactly()
    {
        await _fixture.PublishSnapshotAsync(
            Rule(PriceAdjustmentRuleType.TypeId, """{"entries":[{"target":{"kind":"Type","id":1},"mode":"PercentOff","value":10}]}"""));
        List<QuoteLine> items = [new QuoteLine(1, 2)];
        try
        {
            var quote = await QuoteAsync(items);

            var line = Assert.Single(quote.Lines);
            Assert.Equal(9.00m, line.UnitPrice);
            Assert.Equal(18.00m, quote.Total);

            var createResponse = await _testBuyerOrders.PostV2Async(Guid.NewGuid(), items, expectedTotal: 18.00m);

            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            var stored = await GetStoredOrderAsync();
            Assert.Equal(18.00m, stored.Total);
            var item = Assert.Single(stored.OrderItems);
            Assert.Equal(10.00m, item.BasePrice);
            Assert.Equal(9.00m, item.UnitPrice);
        }
        finally
        {
            await _testBuyerOrders.DeleteAllAsync();
        }
    }

    [Fact]
    public async Task PromotionLabelAndShippingAreStoredOnOrder()
    {
        await _fixture.PublishSnapshotAsync(
            Rule(PromotionRuleType.TypeId, """{"linePromotions":[{"target":{"kind":"Item","id":2},"kind":"PercentOff","value":10,"label":"10% off"}],"orderPromotions":[]}"""),
            Rule(ShippingFeeRuleType.TypeId, """{"fee":4.99,"freeShippingThreshold":null}"""));
        List<QuoteLine> items = [new QuoteLine(2, 1)];
        try
        {
            var quote = await QuoteAsync(items);
            Assert.Equal(4.99m, quote.ShippingFee);

            var createResponse = await _testBuyerOrders.PostV2Async(Guid.NewGuid(), items, quote.Total);

            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            var order = await _testBuyerOrders.GetSingleAsync();
            Assert.Equal(4.99m, order.ShippingFee);
            Assert.Equal(quote.Total, order.Total);
            var item = Assert.Single(order.OrderItems);
            Assert.Equal("10% off", item.PromotionLabel);
            Assert.Equal(2.05m, item.Discount);
        }
        finally
        {
            await _testBuyerOrders.DeleteAllAsync();
        }
    }

    [Fact]
    public async Task OrderStoresResolvedGracePeriodAndShortfallPolicy()
    {
        await _fixture.PublishSnapshotAsync(
            Rule(GracePeriodRuleType.TypeId, """{"minutes":10}"""),
            Rule(StockShortfallPolicyRuleType.TypeId, """{"policy":"PartialFulfilment"}"""));
        List<QuoteLine> items = [new QuoteLine(1, 1)];
        try
        {
            var createResponse = await _testBuyerOrders.PostV2Async(Guid.NewGuid(), items, expectedTotal: 10.00m);

            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            var stored = await GetStoredOrderAsync();
            Assert.Equal(TimeSpan.FromMinutes(10), stored.GracePeriodEndsAt - stored.OrderDate);
            Assert.Equal(StockShortfallPolicy.PartialFulfilment, stored.StockShortfallPolicy);
        }
        finally
        {
            await _testBuyerOrders.DeleteAllAsync();
        }
    }

    private static SnapshotRuleVersion Rule(string ruleTypeId, string parametersJson)
        => new(ruleTypeId, RuleScopes.Default, 1, DateTimeOffset.UtcNow.AddDays(-1), null, JsonDocument.Parse(parametersJson).RootElement.Clone());

    private async Task<PriceQuote> QuoteAsync(List<QuoteLine> items)
    {
        var response = await _httpClientV1.PostAsJsonAsync("api/pricing/quote", new { items }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<PriceQuote>(TestContext.Current.CancellationToken);
        Assert.NotNull(quote);
        return quote;
    }

    private async Task<Order> GetStoredOrderAsync()
    {
        var summary = Assert.Single(await _testBuyerOrders.GetAllAsync());
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
        return await context.Orders.Include(o => o.OrderItems).SingleAsync(o => o.Id == summary.OrderNumber, TestContext.Current.CancellationToken);
    }
}
