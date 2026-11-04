using eShop.BusinessRules.Client.Pricing;

namespace eShop.Ordering.API.Application.Pricing;

public interface IOrderPricingService
{
    /// <summary>Prices <paramref name="lines"/> at current catalog prices with the cached rules snapshot.</summary>
    /// <exception cref="UnknownProductsException">The catalog doesn't know one or more products.</exception>
    /// <exception cref="ArgumentException">There are no lines, or a line has units of zero or less.</exception>
    /// <exception cref="CatalogUnavailableException">Catalog prices couldn't be retrieved.</exception>
    Task<PriceQuote> QuoteAsync(IReadOnlyList<QuoteLine> lines, CancellationToken ct);
}
