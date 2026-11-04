using System.Collections.Concurrent;
using System.Threading;
using eShop.BusinessRules.Client.Pricing;
using eShop.Ordering.API.Application.Pricing;

namespace eShop.Ordering.FunctionalTests;

/// <summary>Stands in for Catalog.API: products 1–5 at fixed prices.</summary>
public sealed class FakeCatalogPriceSource : ICatalogPriceSource
{
    /// <summary>Asking for this product id simulates Catalog.API being unreachable.</summary>
    public const int UnavailableProductId = 9_999;

    public static IReadOnlyDictionary<int, CatalogPrice> Products { get; } = new Dictionary<int, CatalogPrice>
    {
        [1] = new(1, "Fake Product 1", 10.00m, 1, 1, null),
        [2] = new(2, "Fake Product 2", 20.50m, 1, 2, null),
        [3] = new(3, "Fake Product 3", 5.25m, 2, 1, null),
        [4] = new(4, "Fake Product 4", 99.99m, 2, 2, null),
        [5] = new(5, "Fake Product 5", 0.99m, 3, 1, null),
    };

    private readonly ConcurrentDictionary<int, decimal> _priceOverrides = new();

    /// <summary>Makes this instance return <paramref name="price"/> for the product until <see cref="ResetPrices"/>.</summary>
    public void OverridePrice(int productId, decimal price) => _priceOverrides[productId] = price;

    public void ResetPrices() => _priceOverrides.Clear();

    public Task<IReadOnlyDictionary<int, CatalogPrice>> GetPricesAsync(IReadOnlyCollection<int> productIds, CancellationToken ct)
    {
        if (productIds.Contains(UnavailableProductId))
        {
            throw new CatalogUnavailableException("Simulated catalog outage.");
        }

        IReadOnlyDictionary<int, CatalogPrice> prices = productIds
            .Distinct()
            .Where(Products.ContainsKey)
            .ToDictionary(id => id, id => _priceOverrides.TryGetValue(id, out var price) ? Products[id] with { BasePrice = price } : Products[id]);

        return Task.FromResult(prices);
    }
}
