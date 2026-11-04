using eShop.BusinessRules.Client.Rules.Types;
using eShop.BusinessRules.Client.Snapshot;

namespace eShop.BusinessRules.Client.Pricing;

/// <summary>
/// Prices a set of lines against catalog base prices and a rules snapshot.
/// Evaluation order: price adjustments → line promotions → order promotion → shipping.
/// With no effective rules every step falls back to today's behaviour (catalog price, no discount, free shipping).
/// </summary>
public sealed class PricingCalculator
{
    private const decimal MinUnitPrice = 0.01m;

    private static readonly PriceAdjustmentRuleType PriceAdjustments = new();

    public PriceQuote Calculate(
        IReadOnlyList<QuoteLine> lines,
        IReadOnlyDictionary<int, CatalogPrice> catalog,
        RulesSnapshot snapshot,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(snapshot);

        var merged = MergeLines(lines);
        EnsureProductsKnown(merged, catalog);

        var priced = merged.Select(line => CreateBaseLine(line, catalog[line.ProductId])).ToList();
        priced = ApplyPriceAdjustments(priced, catalog, snapshot, now);
        priced = ApplyLinePromotions(priced, snapshot, now);
        priced = ApplyOrderPromotion(priced, snapshot, now);

        var subtotal = priced.Sum(l => l.Units * l.UnitPrice);
        var discountTotal = priced.Sum(l => l.Discount);
        var shippingFee = ApplyShipping(priced, subtotal - discountTotal, snapshot, now);

        return new PriceQuote(
            priced,
            subtotal,
            discountTotal,
            shippingFee,
            subtotal - discountTotal + shippingFee,
            snapshot.Version);
    }

    private static List<QuoteLine> MergeLines(IReadOnlyList<QuoteLine> lines)
    {
        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one line is required.", nameof(lines));
        }

        var merged = new List<QuoteLine>();
        var indexByProduct = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Units <= 0)
            {
                throw new ArgumentException($"Units for product {line.ProductId} must be greater than zero.", nameof(lines));
            }

            if (indexByProduct.TryGetValue(line.ProductId, out var index))
            {
                merged[index] = merged[index] with { Units = checked(merged[index].Units + line.Units) };
            }
            else
            {
                indexByProduct[line.ProductId] = merged.Count;
                merged.Add(line);
            }
        }

        return merged;
    }

    private static void EnsureProductsKnown(List<QuoteLine> lines, IReadOnlyDictionary<int, CatalogPrice> catalog)
    {
        var unknown = lines
            .Select(l => l.ProductId)
            .Where(id => !catalog.ContainsKey(id))
            .Order()
            .ToList();

        if (unknown.Count > 0)
        {
            throw new UnknownProductsException(unknown);
        }
    }

    private static PricedLine CreateBaseLine(QuoteLine line, CatalogPrice price)
    {
        var basePrice = PriceRounding.Round(price.BasePrice);
        return new(
            price.ProductId,
            price.ProductName,
            price.PictureUrl,
            line.Units,
            basePrice,
            UnitPrice: basePrice,
            Discount: 0m,
            PromotionLabel: null);
    }

    private static List<PricedLine> ApplyPriceAdjustments(
        List<PricedLine> lines,
        IReadOnlyDictionary<int, CatalogPrice> catalog,
        RulesSnapshot snapshot,
        DateTimeOffset now)
    {
        var entries = PriceAdjustments.Resolve(snapshot, now).Entries;
        if (entries.Count == 0)
        {
            return lines;
        }

        return [.. lines.Select(line => line with { UnitPrice = AdjustedUnitPrice(line, catalog[line.ProductId], entries) })];
    }

    private static decimal AdjustedUnitPrice(PricedLine line, CatalogPrice product, IReadOnlyList<PriceAdjustmentEntry> entries)
    {
        var entry = entries
            .Where(e => e.Target.Matches(product))
            .MaxBy(e => e.Target.Specificity);
        if (entry is null)
        {
            return line.BasePrice;
        }

        var adjusted = entry.Mode == PriceAdjustmentMode.PercentOff
            ? line.BasePrice * (1m - entry.Value / 100m)
            : entry.Value;
        return Math.Max(MinUnitPrice, PriceRounding.Round(adjusted));
    }

    // Phase 2 (PRM-001): set per-line Discount and PromotionLabel.
    private static List<PricedLine> ApplyLinePromotions(List<PricedLine> lines, RulesSnapshot snapshot, DateTimeOffset now) => lines;

    // Phase 2: distribute an order-level promotion across line discounts.
    private static List<PricedLine> ApplyOrderPromotion(List<PricedLine> lines, RulesSnapshot snapshot, DateTimeOffset now) => lines;

    // Phase 2 (SHP-001): compute the shipping fee from the discounted subtotal.
    private static decimal ApplyShipping(List<PricedLine> lines, decimal discountedSubtotal, RulesSnapshot snapshot, DateTimeOffset now) => 0m;
}
