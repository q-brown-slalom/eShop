namespace eShop.Ordering.UnitTests.Application;

using System.Diagnostics.Metrics;
using System.Text.Json;
using eShop.BusinessRules.Client.Pricing;
using eShop.BusinessRules.Client.Rules;
using eShop.BusinessRules.Client.Snapshot;
using eShop.Ordering.API.Application.Pricing;
using eShop.Ordering.API.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public class OrderPricingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly ICatalogPriceSource _catalog = Substitute.For<ICatalogPriceSource>();
    private readonly IRulesSnapshotSource _snapshotSource = Substitute.For<IRulesSnapshotSource>();
    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();

    [TestCleanup]
    public void Cleanup() => _services.Dispose();

    [TestMethod]
    public async Task QuoteUsesCurrentSnapshotVersion()
    {
        GivenCatalog(new CatalogPrice(1, "Product 1", 10.00m, 1, 1, null));
        var cache = await CreateCacheAtVersionAsync(5);
        var service = CreateService(cache);

        var quote = await service.QuoteAsync([new QuoteLine(1, 2)], CancellationToken.None);

        Assert.AreEqual(5, quote.SnapshotVersion);
        Assert.AreEqual(20.00m, quote.Total);
    }

    [TestMethod]
    public async Task QuoteUsesDefaultsWhenNoSnapshotLoaded()
    {
        GivenCatalog(new CatalogPrice(1, "Product 1", 10.00m, 1, 1, null));
        var service = CreateService(CreateCache());

        var quote = await service.QuoteAsync([new QuoteLine(1, 2)], CancellationToken.None);

        Assert.AreEqual(0, quote.SnapshotVersion);
        Assert.AreEqual(20.00m, quote.Total);
    }

    [TestMethod]
    public async Task QuoteRequestsEachProductOnce()
    {
        GivenCatalog(
            new CatalogPrice(1, "Product 1", 10.00m, 1, 1, null),
            new CatalogPrice(2, "Product 2", 1.00m, 1, 1, null));
        var service = CreateService(CreateCache());

        var quote = await service.QuoteAsync([new QuoteLine(1, 1), new QuoteLine(2, 1), new QuoteLine(1, 3)], CancellationToken.None);

        await _catalog.Received(1).GetPricesAsync(
            Arg.Is<IReadOnlyCollection<int>>(ids => ids.Count == 2 && ids.Contains(1) && ids.Contains(2)),
            Arg.Any<CancellationToken>());
        Assert.AreEqual(41.00m, quote.Total);
    }

    [TestMethod]
    public async Task QuoteRoundsAmountsToTwoDecimals()
    {
        GivenCatalog(new CatalogPrice(1, "Product 1", 10.005m, 1, 1, null));
        var service = CreateService(CreateCache());

        var quote = await service.QuoteAsync([new QuoteLine(1, 3)], CancellationToken.None);

        var line = quote.Lines.Single();
        Assert.AreEqual(10.01m, line.BasePrice);
        Assert.AreEqual(10.01m, line.UnitPrice);
        Assert.AreEqual(30.03m, quote.Subtotal);
        Assert.AreEqual(30.03m, quote.Total);
    }

    [TestMethod]
    public async Task QuoteReturnsCalculatorRoundingUnchanged()
    {
        var product = new CatalogPrice(1, "Product 1", 10.00m, 1, 1, null);
        GivenCatalog(product);
        var snapshot = new RulesSnapshot(
            3,
            Now.AddMinutes(-1),
            [new SnapshotRuleVersion(
                "PRC-001",
                RuleScopes.Default,
                1,
                Now.AddHours(-1),
                null,
                JsonDocument.Parse("""{"entries":[{"target":{"kind":"All"},"mode":"PercentOff","value":33}]}""").RootElement.Clone())]);
        _snapshotSource.FetchAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(new SnapshotFetchResult(false, snapshot));
        var cache = CreateCache();
        Assert.IsTrue(await cache.RefreshAsync(CancellationToken.None));
        var service = CreateService(cache);

        var quote = await service.QuoteAsync([new QuoteLine(1, 3)], CancellationToken.None);

        var expected = new PricingCalculator().Calculate([new QuoteLine(1, 3)], new Dictionary<int, CatalogPrice> { [1] = product }, snapshot, Now);
        Assert.AreEqual(6.70m, quote.Lines.Single().UnitPrice);
        Assert.AreEqual(expected.Lines.Single(), quote.Lines.Single());
        Assert.AreEqual(expected.Subtotal, quote.Subtotal);
        Assert.AreEqual(expected.Total, quote.Total);
        Assert.AreEqual(3, quote.SnapshotVersion);
    }

    [TestMethod]
    public async Task QuoteWithUnknownProductThrowsUnknownProducts()
    {
        GivenCatalog(new CatalogPrice(1, "Product 1", 10.00m, 1, 1, null));
        var service = CreateService(CreateCache());

        var ex = await Assert.ThrowsExactlyAsync<UnknownProductsException>(
            () => service.QuoteAsync([new QuoteLine(1, 1), new QuoteLine(42, 1)], CancellationToken.None));

        CollectionAssert.AreEqual(new[] { 42 }, ex.ProductIds.ToArray());
    }

    [TestMethod]
    public async Task QuoteRecordsDuration()
    {
        GivenCatalog(new CatalogPrice(1, "Product 1", 10.00m, 1, 1, null));
        var meterFactory = _services.GetRequiredService<IMeterFactory>();
        var recorded = new List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, meterFactory) && instrument.Name == "pricing.quote.duration")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => recorded.Add(value));
        listener.Start();
        var service = CreateService(CreateCache());

        await service.QuoteAsync([new QuoteLine(1, 1)], CancellationToken.None);

        Assert.HasCount(1, recorded);
        Assert.IsGreaterThanOrEqualTo(0d, recorded[0]);
    }

    private void GivenCatalog(params CatalogPrice[] prices)
    {
        _catalog.GetPricesAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ids = call.Arg<IReadOnlyCollection<int>>();
                IReadOnlyDictionary<int, CatalogPrice> found = prices.Where(p => ids.Contains(p.ProductId)).ToDictionary(p => p.ProductId);
                return Task.FromResult(found);
            });
    }

    private RulesSnapshotCache CreateCache()
        => new(_snapshotSource, new FixedTimeProvider(Now), NullLogger<RulesSnapshotCache>.Instance);

    private async Task<RulesSnapshotCache> CreateCacheAtVersionAsync(long version)
    {
        _snapshotSource.FetchAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(new SnapshotFetchResult(false, new RulesSnapshot(version, Now.AddMinutes(-1), [])));
        var cache = CreateCache();
        Assert.IsTrue(await cache.RefreshAsync(CancellationToken.None));
        return cache;
    }

    private OrderPricingService CreateService(RulesSnapshotCache cache)
        => new(
            _catalog,
            new PricingCalculator(),
            cache,
            new FixedTimeProvider(Now),
            new OrderingMetrics(_services.GetRequiredService<IMeterFactory>()));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
