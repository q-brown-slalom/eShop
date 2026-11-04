namespace eShop.Ordering.API.Extensions;

public static class BasketItemExtensions
{
    public static OrderItemDTO ToOrderItemDTO(this BasketItem item)
    {
        return new OrderItemDTO()
        {
            ProductId = item.ProductId,
            ProductName = item.ProductName,
            PictureUrl = item.PictureUrl,
            UnitPrice = item.UnitPrice,
            Units = item.Quantity
        };
    }
}
