extern alias OrderProcessorAssembly;

using System.Net;
using System.Text.Json;
using eShop.BusinessRules.Client.Pricing;
using eShop.BusinessRules.Client.Rules;
using eShop.BusinessRules.Client.Rules.Types;
using eShop.BusinessRules.Client.Snapshot;
using eShop.Ordering.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OrderProcessorAssembly::eShop.OrderProcessor.Services;

namespace eShop.Ordering.FunctionalTests;

/// <summary>ORD-001: OrderProcessor confirms an order once its own <c>GracePeriodEndsAt</c> has passed.</summary>
public sealed class GracePeriodOrdersRepositoryTests : IClassFixture<OrderingApiFixture>
{
    private readonly OrderingApiFixture _fixture;
    private readonly TestBuyerOrders _testBuyerOrders;

    public GracePeriodOrdersRepositoryTests(OrderingApiFixture fixture)
    {
        _fixture = fixture;
        _testBuyerOrders = new TestBuyerOrders(fixture);
    }

    [Fact]
    public async Task GracePeriodRepositoryReturnsOnlyOrdersPastTheirOwnGracePeriod()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _fixture.PublishSnapshotAsync(
            new SnapshotRuleVersion(
                GracePeriodRuleType.TypeId,
                RuleScopes.Default,
                1,
                DateTimeOffset.UtcNow.AddDays(-1),
                null,
                JsonDocument.Parse("""{"minutes":10}""").RootElement.Clone()));
        try
        {
            await CreateOrderAsync();
            await CreateOrderAsync();
            var orderIds = await GetOrderIdsAsync();
            Assert.Equal(2, orderIds.Count);
            var expiredOrderId = orderIds[0];
            using var scope = _fixture.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
            await context.Database.ExecuteSqlAsync(
                $"""UPDATE ordering.orders SET "GracePeriodEndsAt" = now() - interval '1 second' WHERE "Id" = {expiredOrderId}""",
                cancellationToken);
            await using var dataSource = NpgsqlDataSource.Create(await _fixture.Postgres.Resource.GetConnectionStringAsync(cancellationToken));
            var repository = new GracePeriodOrdersRepository(dataSource, NullLogger<GracePeriodOrdersRepository>.Instance);

            var confirmed = await repository.GetConfirmedGracePeriodOrdersAsync(cancellationToken);

            Assert.Equal([expiredOrderId], confirmed);
        }
        finally
        {
            await _testBuyerOrders.DeleteAllAsync();
        }
    }

    private async Task CreateOrderAsync()
    {
        var response = await _testBuyerOrders.PostV2Async(Guid.NewGuid(), [new QuoteLine(1, 1)], expectedTotal: 10.00m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<List<int>> GetOrderIdsAsync()
        => (await _testBuyerOrders.GetAllAsync()).Select(o => o.OrderNumber).OrderBy(id => id).ToList();
}
