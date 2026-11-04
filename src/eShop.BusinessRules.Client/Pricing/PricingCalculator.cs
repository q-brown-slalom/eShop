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

    private static readonly PromotionRuleType Promotions = new();

    private static readonly ShippingFeeRuleType Shipping = new();

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
        priced = ApplyLinePromotions(priced, catalog, snapshot, now);
        (priced, var orderPromotionLabel) = ApplyOrderPromotion(priced, snapshot, now);

        var subtotal = priced.Sum(l => l.Units * l.UnitPrice);
        var discountTotal = priced.Sum(l => l.Discount);
        var shippingFee = ApplyShipping(subtotal - discountTotal, snapshot, now);

        return new PriceQuote(
            priced,
            subtotal,
            discountTotal,
            shippingFee,
            subtotal - discountTotal + shippingFee,
            snapshot.Version,
            orderPromotionLabel);
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

    private static List<PricedLine> ApplyLinePromotions(
        List<PricedLine> lines,
        IReadOnlyDictionary<int, CatalogPrice> catalog,
        RulesSnapshot snapshot,
        DateTimeOffset now)
    {
        var entries = Promotions.Resolve(snapshot, now).LinePromotions;
        if (entries.Count == 0)
        {
            return lines;
        }

        return [.. lines.Select(line => WithLinePromotion(line, catalog[line.ProductId], entries))];
    }

    // Most specific applicable target wins; among those the highest discount; on a tie the first listed.
    private static PricedLine WithLinePromotion(PricedLine line, CatalogPrice product, IReadOnlyList<LinePromotion> entries)
    {
        var applicable = entries
            .Where(e => e.Target.Matches(product) && (e.MinUnits is null || line.Units >= e.MinUnits))
            .ToList();
        if (applicable.Count == 0)
        {
            return line;
        }

        var specificity = applicable.Max(e => e.Target.Specificity);
        var best = applicable
            .Where(e => e.Target.Specificity == specificity)
            .Select(e => (Entry: e, Amount: LineDiscount(line, e)))
            .MaxBy(c => c.Amount);

        return best.Amount > 0m ? line with { Discount = best.Amount, PromotionLabel = best.Entry.Label } : line;
    }

    private static decimal LineDiscount(PricedLine line, LinePromotion promotion)
    {
        var lineSubtotal = line.Units * line.UnitPrice;
        var amount = promotion.Kind == DiscountKind.PercentOff
            ? lineSubtotal * promotion.Value / 100m
            : line.Units * promotion.Value;
        return Math.Min(lineSubtotal, PriceRounding.Round(amount));
    }

    private static (List<PricedLine> Lines, string? Label) ApplyOrderPromotion(List<PricedLine> lines, RulesSnapshot snapshot, DateTimeOffset now)
    {
        var entries = Promotions.Resolve(snapshot, now).OrderPromotions;
        var subtotal = lines.Sum(l => l.LineTotal);
        var applicable = entries
            .Where(e => subtotal >= e.MinSubtotal)
            .Select(e => (Entry: e, Amount: OrderDiscount(e, subtotal)))
            .Where(c => c.Amount > 0m)
            .ToList();
        if (applicable.Count == 0)
        {
            return (lines, null);
        }

        var best = applicable.MaxBy(c => c.Amount);
        var shares = DiscountAllocation.Allocate(best.Amount, [.. lines.Select(l => l.LineTotal)]);
        var allocated = lines
            .Select((line, i) => line with { Discount = line.Discount + shares[i], OrderDiscountShare = shares[i] })
            .ToList();
        return (allocated, best.Entry.Label);
    }

    private static decimal OrderDiscount(OrderPromotion promotion, decimal subtotal)
    {
        var amount = promotion.Kind == DiscountKind.PercentOff
            ? subtotal * promotion.Value / 100m
            : promotion.Value;
        return Math.Min(subtotal, PriceRounding.Round(amount));
    }

    private static decimal ApplyShipping(decimal discountedSubtotal, RulesSnapshot snapshot, DateTimeOffset now)
    {
        var shipping = Shipping.Resolve(snapshot, now);
        return shipping.FreeShippingThreshold is { } threshold && discountedSubtotal >= threshold
            ? 0m
            : PriceRounding.Round(shipping.Fee);
    }
}
