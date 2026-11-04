using eShop.BusinessRules.Client.Pricing;

namespace eShop.WebApp.Services;

/// <summary>Thrown when Ordering.API rejects an order because its total no longer matches the server quote.</summary>
public class PricesChangedException(PriceQuote quote)
    : Exception("Prices changed since the order total was shown.")
{
    public PriceQuote Quote { get; } = quote;
}
