using System.Net;
using System.Text.Json;
using eShop.BusinessRules.Client.Pricing;
using eShop.BusinessRules.Client.Rules;
using eShop.BusinessRules.Client.Rules.Types;
using eShop.BusinessRules.Client.Snapshot;
using eShop.IntegrationEventLogEF;
using eShop.Ordering.API.Application.Commands;
using eShop.Ordering.API.Application.IntegrationEvents.Events;
using eShop.Ordering.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace eShop.Ordering.FunctionalTests;

/// <summary>ORD-013: a stock rejection reprices and pays the order without the rejected line under PartialFulfilment.</summary>
public sealed class PartialFulfilmentTests : IClassFixture<OrderingApiFixture>
{
    private const decimal ThreeLineTotal = 35.75m;
    private const decimal ProductTwoLineTotal = 20.50m;

    private readonly OrderingApiFixture _fixture;
    private readonly TestBuyerOrders _testBuyerOrders;

    public PartialFulfilmentTests(OrderingApiFixture fixture)
    {
        _fixture = fixture;
        _testBuyerOrders = new TestBuyerOrders(fixture);
    }

    [Fact]
    public async Task PartialFulfilmentRepricesOrderAndExcludesDroppedLineFromPaidEvent()
    {
        await _fixture.PublishSnapshotAsync(PartialFulfilmentRule());
        try
        {
            var orderId = await CreateOrderAndRejectProductTwoAsync();

            var order = await _testBuyerOrders.GetSingleAsync();
            Assert.Equal("Paid", order.Status);
            Assert.Equal(ThreeLineTotal - ProductTwoLineTotal, order.Total);
            Assert.Equal(
                [false, true, false],
                order.OrderItems.OrderBy(i => i.ProductName).Select(i => i.IsUnfulfillable));
            Assert.Equal(20.50m, Assert.Single(order.OrderItems, i => i.IsUnfulfillable).BasePrice);

            var paidEvent = await GetPaidEventAsync(orderId);
            Assert.Equal([1, 3], paidEvent.OrderStockItems.Select(i => i.ProductId).OrderBy(id => id));
        }
        finally
        {
            await _testBuyerOrders.DeleteAllAsync();
        }
    }

    [Fact]
    public async Task GetOrdersListsDroppedItems()
    {
        await _fixture.PublishSnapshotAsync(PartialFulfilmentRule());
        try
        {
            await CreateOrderAndRejectProductTwoAsync();

            var summary = Assert.Single(await _testBuyerOrders.GetAllAsync());
            Assert.Equal(["Fake Product 2"], summary.DroppedItems);
        }
        finally
        {
            await _testBuyerOrders.DeleteAllAsync();
        }
    }

    [Fact]
    public async Task AllOrNothingStockRejectionStillCancels()
    {
        await _fixture.PublishSnapshotAsync();
        try
        {
            var orderId = await CreateOrderAsync();
            await SendAsync(new SetAwaitingValidationOrderStatusCommand(orderId));
            await SendAsync(new SetStockRejectedOrderStatusCommand(orderId, [2]));

            var summary = Assert.Single(await _testBuyerOrders.GetAllAsync());
            Assert.Equal("Cancelled", summary.Status);
            Assert.Empty(summary.DroppedItems);
        }
        finally
        {
            await _testBuyerOrders.DeleteAllAsync();
        }
    }

    private static SnapshotRuleVersion PartialFulfilmentRule()
        => new(StockShortfallPolicyRuleType.TypeId, RuleScopes.Default, 1, DateTimeOffset.UtcNow.AddDays(-1), null,
            JsonDocument.Parse("""{"policy":"PartialFulfilment"}""").RootElement.Clone());

    private async Task<int> CreateOrderAsync()
    {
        List<QuoteLine> items = [new QuoteLine(1, 1), new QuoteLine(2, 1), new QuoteLine(3, 1)];
        var response = await _testBuyerOrders.PostV2Async(Guid.NewGuid(), items, ThreeLineTotal);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single(await _testBuyerOrders.GetAllAsync()).OrderNumber;
    }

    private async Task<int> CreateOrderAndRejectProductTwoAsync()
    {
        var orderId = await CreateOrderAsync();
        await SendAsync(new SetAwaitingValidationOrderStatusCommand(orderId));
        await SendAsync(new SetStockRejectedOrderStatusCommand(orderId, [2]));
        await SendAsync(new SetPaidOrderStatusCommand(orderId));
        return orderId;
    }

    private async Task SendAsync(IRequest<bool> command)
    {
        using var scope = _fixture.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        Assert.True(await mediator.Send(command, TestContext.Current.CancellationToken));
    }

    private async Task<OrderStatusChangedToPaidIntegrationEvent> GetPaidEventAsync(int orderId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
        var entries = await context.Set<IntegrationEventLogEntry>()
            .Where(e => e.EventTypeName == typeof(OrderStatusChangedToPaidIntegrationEvent).FullName)
            .ToListAsync(TestContext.Current.CancellationToken);
        return entries
            .Select(e => e.DeserializeJsonContent(typeof(OrderStatusChangedToPaidIntegrationEvent)).IntegrationEvent)
            .OfType<OrderStatusChangedToPaidIntegrationEvent>()
            .Single(e => e.OrderId == orderId);
    }
}
