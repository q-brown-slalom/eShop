using eShop.BusinessRules.Client.Pricing;

namespace eShop.Ordering.API.Application.Pricing;

/// <summary>
/// Reads base prices from Catalog.API (<c>GET api/catalog/items/by</c>). Retries, timeouts and circuit breaking
/// come from the standard resilience handler that ServiceDefaults adds to every HttpClient.
/// </summary>
public sealed class CatalogPriceClient(HttpClient httpClient) : ICatalogPriceSource
{
    public const string BaseAddress = "https+http://catalog-api";

    public async Task<IReadOnlyDictionary<int, CatalogPrice>> GetPricesAsync(IReadOnlyCollection<int> productIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        if (productIds.Count == 0)
        {
            return new Dictionary<int, CatalogPrice>();
        }

        var ids = string.Join('&', productIds.Distinct().Select(id => $"ids={id}"));

        List<CatalogItemResponse> items;
        try
        {
            items = await httpClient.GetFromJsonAsync<List<CatalogItemResponse>>($"api/catalog/items/by?{ids}&api-version=1.0", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CatalogUnavailableException("Catalog prices could not be retrieved.", ex);
        }

        if (items is null)
        {
            throw new CatalogUnavailableException("Catalog returned no item list.");
        }

        return items.ToDictionary(
            item => item.Id,
            item => new CatalogPrice(item.Id, item.Name, item.Price, item.CatalogTypeId, item.CatalogBrandId, PictureUrl: null));
    }

    private sealed record CatalogItemResponse(int Id, string Name, decimal Price, int CatalogTypeId, int CatalogBrandId);
}
