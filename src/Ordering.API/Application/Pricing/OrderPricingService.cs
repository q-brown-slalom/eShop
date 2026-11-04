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

            var quote = calculator.Calculate(lines, catalog, snapshotCache.Current, timeProvider.GetUtcNow());
            return RoundToCents(quote);
        }
        finally
        {
            metrics.QuoteDuration(timeProvider.GetElapsedTime(started));
        }
    }

    // Order totals assume 2-decimal inputs, so round per line and rebuild the totals from the rounded lines.
    private static PriceQuote RoundToCents(PriceQuote quote)
    {
        var lines = quote.Lines
            .Select(l => l with
            {
                BasePrice = PriceRounding.Round(l.BasePrice),
                UnitPrice = PriceRounding.Round(l.UnitPrice),
                Discount = PriceRounding.Round(l.Discount),
            })
            .ToList();

        var subtotal = lines.Sum(l => l.Units * l.UnitPrice);
        var discountTotal = lines.Sum(l => l.Discount);
        var shippingFee = PriceRounding.Round(quote.ShippingFee);

        return quote with
        {
            Lines = lines,
            Subtotal = subtotal,
            DiscountTotal = discountTotal,
            ShippingFee = shippingFee,
            Total = subtotal - discountTotal + shippingFee,
        };
    }
}
