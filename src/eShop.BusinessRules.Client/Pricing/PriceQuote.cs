namespace eShop.BusinessRules.Client.Pricing;

/// <param name="Subtotal">Σ Units × UnitPrice.</param>
/// <param name="DiscountTotal">Σ line Discount.</param>
/// <param name="Total">Subtotal − DiscountTotal + ShippingFee.</param>
public sealed record PriceQuote(
    IReadOnlyList<PricedLine> Lines,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal ShippingFee,
    decimal Total,
    long SnapshotVersion);
