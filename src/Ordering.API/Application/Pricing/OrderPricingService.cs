using eShop.BusinessRules.Client.Pricing;
using eShop.BusinessRules.Client.Snapshot;

namespace eShop.Ordering.API.Application.Pricing;

/// <summary>
/// Prices orders server-side. Uses the locally cached rules snapshot only, so pricing never calls
/// BusinessRules.API and still works at catalog prices when no snapshot has been loaded.
/// </summary>
public sealed class OrderPricingService(
    ICatalogPriceSource catalogPrices,
    PricingCalculator calculator,
    RulesSnapshotCache snapshotCache,
    TimeProvider timeProvider,
    OrderingMetrics metrics) : IOrderPricingService
{
    public async Task<PriceQuote> QuoteAsync(IReadOnlyList<QuoteLine> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var started = timeProvider.GetTimestamp();
        try
        {
            var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
            var catalog = await catalogPrices.GetPricesAsync(productIds, ct);

            return calculator.Calculate(lines, catalog, snapshotCache.Current, timeProvider.GetUtcNow());
        }
        finally
        {
            metrics.QuoteDuration(timeProvider.GetElapsedTime(started));
        }
    }
}
