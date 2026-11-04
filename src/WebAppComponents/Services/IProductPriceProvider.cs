namespace eShop.WebAppComponents.Services;

public interface IProductPriceProvider
{
    /// <summary>
    /// Returns the price to show for one unit of each product. Products without a price are left out,
    /// so callers fall back to the catalog price.
    /// </summary>
    Task<IReadOnlyDictionary<int, decimal>> GetDisplayPricesAsync(IReadOnlyCollection<int> productIds);
}
