using System.ComponentModel.DataAnnotations;

namespace eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

public class OrderItem
    : Entity
{
    [Required]
    public string ProductName { get; private set; }
    
    public string PictureUrl { get; private set;}
    
    public decimal UnitPrice { get; private set;}

    public decimal BasePrice { get; private set; }
    
    public decimal Discount { get; private set; }
    
    public int Units { get; private set; }

    public int ProductId { get; private set; }

    public string PromotionLabel { get; private set; }

    // The part of Discount that came from the order-level promotion.
    public decimal OrderDiscountShare { get; private set; }

    public bool IsUnfulfillable { get; private set; }

    protected OrderItem() { }

    public OrderItem(int productId, string productName, decimal unitPrice, decimal discount, string pictureUrl, int units = 1, decimal? basePrice = null, string promotionLabel = null, decimal orderDiscountShare = 0m)
    {
        if (units <= 0)
        {
            throw new OrderingDomainException("Invalid number of units");
        }

        if (unitPrice < 0.01m)
        {
            throw new OrderingDomainException("The unit price must be at least 0.01");
        }

        if ((unitPrice * units) < discount)
        {
            throw new OrderingDomainException("The total of order item is lower than applied discount");
        }

        if (orderDiscountShare < 0 || orderDiscountShare > discount)
        {
            throw new OrderingDomainException("The order discount share must be between 0 and the line discount");
        }

        ProductId = productId;

        ProductName = productName;
        UnitPrice = unitPrice;
        BasePrice = basePrice ?? unitPrice;
        Discount = discount;
        Units = units;
        PictureUrl = pictureUrl;
        PromotionLabel = promotionLabel;
        OrderDiscountShare = orderDiscountShare;
    }

    internal void MarkUnfulfillable() => IsUnfulfillable = true;

    internal void ReplaceOrderDiscountShare(decimal share)
    {
        var discount = Discount - OrderDiscountShare + share;

        if (share < 0 || discount > Units * UnitPrice)
        {
            throw new OrderingDomainException("The order discount share is not valid for this order item");
        }

        Discount = discount;
        OrderDiscountShare = share;
    }
    
    public void SetNewDiscount(decimal discount)
    {
        if (discount < 0)
        {
            throw new OrderingDomainException("Discount is not valid");
        }

        Discount = discount;
    }

    public void AddUnits(int units)
    {
        if (units < 0)
        {
            throw new OrderingDomainException("Invalid units");
        }

        Units += units;
    }
}
