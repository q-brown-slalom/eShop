using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

namespace eShop.Ordering.UnitTests.Domain;

public class AddressBuilder
{
    public Address Build()
    {
        return new Address("street", "city", "state", "country", "zipcode");
    }
}

public class OrderBuilder
{
    private readonly Order order;

    public OrderBuilder(Address address)
    {
        order = new Order(
            "userId",
            "fakeName",
            address,
            cardTypeId: 5,
            cardNumber: "12",
            cardSecurityNumber: "123",
            cardHolderName: "name",
            cardExpiration: DateTime.UtcNow);
    }

    public OrderBuilder AddOne(
        int productId,
        string productName,
        decimal unitPrice,
        decimal discount,
        string pictureUrl,
        int units = 1,
        decimal? basePrice = null,
        string promotionLabel = null,
        decimal orderDiscountShare = 0m)
    {
        order.AddOrderItem(productId, productName, unitPrice, discount, pictureUrl, units, basePrice, promotionLabel, orderDiscountShare);
        return this;
    }

    public OrderBuilder WithFulfilmentRules(TimeSpan gracePeriod, StockShortfallPolicy policy)
    {
        order.SetFulfilmentRules(gracePeriod, policy);
        return this;
    }

    public OrderBuilder WithPricing(decimal shippingFee, long pricingSnapshotVersion)
    {
        order.ApplyPricing(shippingFee, pricingSnapshotVersion);
        return this;
    }

    public Order Build()
    {
        return order;
    }
}
