using eShop.BusinessRules.Client.Pricing;

namespace eShop.Ordering.API.Application.Pricing;

/// <summary>Reads current base prices from Catalog.API.</summary>
public interface ICatalogPriceSource
{
    /// <returns>Prices keyed by product id. Ids the catalog doesn't know are absent.</returns>
    /// <exception cref="CatalogUnavailableException">The catalog couldn't be reached or answered with an error.</exception>
    Task<IReadOnlyDictionary<int, CatalogPrice>> GetPricesAsync(IReadOnlyCollection<int> productIds, CancellationToken ct);
}
