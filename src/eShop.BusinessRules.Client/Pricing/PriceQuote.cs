namespace eShop.BusinessRules.Client.Pricing;

/// <param name="Subtotal">Σ Units × UnitPrice.</param>
/// <param name="DiscountTotal">Σ line Discount, including each line's share of the order promotion.</param>
/// <param name="Total">Subtotal − DiscountTotal + ShippingFee.</param>
/// <param name="OrderPromotionLabel">Label of the applied order promotion, if any.</param>
public sealed record PriceQuote(
    IReadOnlyList<PricedLine> Lines,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal ShippingFee,
    decimal Total,
    long SnapshotVersion,
    string? OrderPromotionLabel = null)
{
    /// <summary>The part of <see cref="DiscountTotal"/> that came from the order promotion.</summary>
    public decimal OrderDiscount => Lines.Sum(l => l.OrderDiscountShare);
}
