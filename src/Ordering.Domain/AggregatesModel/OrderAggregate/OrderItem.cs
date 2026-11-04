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

    protected OrderItem() { }

    public OrderItem(int productId, string productName, decimal unitPrice, decimal discount, string pictureUrl, int units = 1, decimal? basePrice = null)
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

        ProductId = productId;

        ProductName = productName;
        UnitPrice = unitPrice;
        BasePrice = basePrice ?? unitPrice;
        Discount = discount;
        Units = units;
        PictureUrl = pictureUrl;
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
