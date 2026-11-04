namespace eShop.BusinessRules.Client.Pricing;

public sealed class UnknownProductsException(IReadOnlyList<int> productIds)
    : Exception($"Unknown product ids: {string.Join(", ", productIds)}.")
{
    public IReadOnlyList<int> ProductIds { get; } = productIds;
}
