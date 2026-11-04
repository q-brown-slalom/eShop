namespace eShop.Ordering.API.Application.Pricing;

/// <summary>Catalog prices couldn't be retrieved, so nothing can be priced right now.</summary>
public sealed class CatalogUnavailableException(string message, Exception innerException = null)
    : Exception(message, innerException);
