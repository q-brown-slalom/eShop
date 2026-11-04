namespace eShop.ClientApp.Models.Pricing;

/// <summary>Server price quote from Ordering.API. Total = Subtotal - DiscountTotal + ShippingFee.</summary>
public sealed record PriceQuote(
    IReadOnlyList<PricedLine> Lines,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal ShippingFee,
    decimal Total,
    long SnapshotVersion);
