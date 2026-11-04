namespace eShop.ClientApp.Models.Pricing;

public sealed record PricedLine(
    int ProductId,
    string ProductName,
    string PictureUrl,
    int Units,
    decimal BasePrice,
    decimal UnitPrice,
    decimal Discount,
    string PromotionLabel)
{
    public decimal LineTotal => Units * UnitPrice - Discount;
}
