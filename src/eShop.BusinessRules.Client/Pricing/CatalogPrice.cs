namespace eShop.BusinessRules.Client.Pricing;

public sealed record CatalogPrice(
    int ProductId,
    string ProductName,
    decimal BasePrice,
    int CatalogTypeId,
    int CatalogBrandId,
    string? PictureUrl);
