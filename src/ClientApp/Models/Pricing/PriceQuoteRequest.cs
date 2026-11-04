namespace eShop.ClientApp.Models.Pricing;

public sealed record PriceQuoteRequest(IReadOnlyList<QuoteLine> Items);
