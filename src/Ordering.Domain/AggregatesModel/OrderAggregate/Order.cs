using System.ComponentModel.DataAnnotations;

namespace eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

public class Order
    : Entity, IAggregateRoot
{
    public DateTime OrderDate { get; private set; }

    // Address is a Value Object pattern example persisted as EF Core 2.0 owned entity
    [Required]
    public Address Address { get; private set; }

    public int? BuyerId { get; private set; }

    public Buyer Buyer { get; }

    public OrderStatus OrderStatus { get; private set; }
    
    public string Description { get; private set; }

    // Draft orders have this set to true. Currently we don't check anywhere the draft status of an Order, but we could do it if needed
#pragma warning disable CS0414 // The field 'Order._isDraft' is assigned but its value is never used
    private bool _isDraft;
#pragma warning restore CS0414

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method OrderAggregateRoot.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems;
   
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public int? PaymentId { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal DiscountTotal { get; private set; }

    public decimal ShippingFee { get; private set; }

    public decimal Total { get; private set; }

    public long PricingSnapshotVersion { get; private set; }

    public static Order NewDraft()
    {
        var order = new Order
        {
            _isDraft = true
        };
        return order;
    }

    protected Order()
    {
        _orderItems = new List<OrderItem>();
        _isDraft = false;
    }

    public Order(string userId, string userName, Address address, int cardTypeId, string cardNumber, string cardSecurityNumber,
            string cardHolderName, DateTime cardExpiration, int? buyerId = null, int? paymentMethodId = null) : this()
    {
        BuyerId = buyerId;
        PaymentId = paymentMethodId;
        OrderStatus = OrderStatus.Submitted;
        OrderDate = DateTime.UtcNow;
        Address = address;

        // Add the OrderStarterDomainEvent to the domain events collection 
        // to be raised/dispatched when committing changes into the Database [ After DbContext.SaveChanges() ]
        AddOrderStartedDomainEvent(userId, userName, cardTypeId, cardNumber,
                                    cardSecurityNumber, cardHolderName, cardExpiration);
    }

    // DDD Patterns comment
    // This Order AggregateRoot's method "AddOrderItem()" should be the only way to add Items to the Order,
    // so any behavior (discounts, etc.) and validations are controlled by the AggregateRoot 
    // in order to maintain consistency between the whole Aggregate. 
    public void AddOrderItem(int productId, string productName, decimal unitPrice, decimal discount, string pictureUrl, int units = 1, decimal? basePrice = null)
    {
        var existingOrderForProduct = _orderItems.SingleOrDefault(o => o.ProductId == productId);

        if (existingOrderForProduct != null)
        {
            //if previous line exist modify it with higher discount  and units..
            if (discount > existingOrderForProduct.Discount)
            {
                existingOrderForProduct.SetNewDiscount(discount);
            }

            existingOrderForProduct.AddUnits(units);
        }
        else
        {
            //add validated new order item
            var orderItem = new OrderItem(productId, productName, unitPrice, discount, pictureUrl, units, basePrice);
            _orderItems.Add(orderItem);
        }

        // Keeps the stored breakdown in step with the lines for orders that are never explicitly priced.
        RecalculateTotals();
    }

    public void ApplyPricing(decimal shippingFee, long pricingSnapshotVersion)
    {
        if (shippingFee < 0)
        {
            throw new OrderingDomainException("The shipping fee cannot be negative");
        }

        var total = _orderItems.Sum(o => o.Units * o.UnitPrice - o.Discount) + shippingFee;

        if (total < 0)
        {
            throw new OrderingDomainException("The order total cannot be negative");
        }

        ShippingFee = shippingFee;
        PricingSnapshotVersion = pricingSnapshotVersion;
        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        Subtotal = _orderItems.Sum(o => o.Units * o.UnitPrice);
        DiscountTotal = _orderItems.Sum(o => o.Discount);
        Total = Subtotal - DiscountTotal + ShippingFee;
    }

    public void SetPaymentMethodVerified(int buyerId, int paymentId)
    {
        BuyerId = buyerId;
        PaymentId = paymentId;
    }
    
    public void SetAwaitingValidationStatus()
    {
        if (OrderStatus == OrderStatus.Submitted)
        {
            AddDomainEvent(new OrderStatusChangedToAwaitingValidationDomainEvent(Id, _orderItems));
            OrderStatus = OrderStatus.AwaitingValidation;
        }
    }

    public void SetStockConfirmedStatus()
    {
        if (OrderStatus == OrderStatus.AwaitingValidation)
        {
            AddDomainEvent(new OrderStatusChangedToStockConfirmedDomainEvent(Id));

            OrderStatus = OrderStatus.StockConfirmed;
            Description = "All the items were confirmed with available stock.";
        }
    }

    public void SetPaidStatus()
    {
        if (OrderStatus == OrderStatus.StockConfirmed)
        {
            AddDomainEvent(new OrderStatusChangedToPaidDomainEvent(Id, OrderItems));

            OrderStatus = OrderStatus.Paid;
            Description = "The payment was performed at a simulated \"American Bank checking bank account ending on XX35071\"";
        }
    }

    public void SetShippedStatus()
    {
        if (OrderStatus != OrderStatus.Paid)
        {
            StatusChangeException(OrderStatus.Shipped);
        }

        OrderStatus = OrderStatus.Shipped;
        Description = "The order was shipped.";
        AddDomainEvent(new OrderShippedDomainEvent(this));
    }

    public void SetCancelledStatus()
    {
        if (OrderStatus == OrderStatus.Paid ||
            OrderStatus == OrderStatus.Shipped)
        {
            StatusChangeException(OrderStatus.Cancelled);
        }

        OrderStatus = OrderStatus.Cancelled;
        Description = "The order was cancelled.";
        AddDomainEvent(new OrderCancelledDomainEvent(this));
    }

    public void SetCancelledStatusWhenStockIsRejected(IEnumerable<int> orderStockRejectedItems)
    {
        if (OrderStatus == OrderStatus.AwaitingValidation)
        {
            OrderStatus = OrderStatus.Cancelled;

            var itemsStockRejectedProductNames = OrderItems
                .Where(c => orderStockRejectedItems.Contains(c.ProductId))
                .Select(c => c.ProductName);

            var itemsStockRejectedDescription = string.Join(", ", itemsStockRejectedProductNames);
            Description = $"The product items don't have stock: ({itemsStockRejectedDescription}).";
        }
    }

    private void AddOrderStartedDomainEvent(string userId, string userName, int cardTypeId, string cardNumber,
            string cardSecurityNumber, string cardHolderName, DateTime cardExpiration)
    {
        var orderStartedDomainEvent = new OrderStartedDomainEvent(this, userId, userName, cardTypeId,
                                                                    cardNumber, cardSecurityNumber,
                                                                    cardHolderName, cardExpiration);

        this.AddDomainEvent(orderStartedDomainEvent);
    }

    private void StatusChangeException(OrderStatus orderStatusToChange)
    {
        throw new OrderingDomainException($"Is not possible to change the order status from {OrderStatus} to {orderStatusToChange}.");
    }

    public decimal GetTotal() => _orderItems.Sum(o => o.Units * o.UnitPrice - o.Discount) + ShippingFee;
}
