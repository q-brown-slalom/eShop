namespace eShop.Ordering.UnitTests.Domain;

using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;
using eShop.Ordering.UnitTests.Domain;

[TestClass]
public class OrderAggregateTest
{
    public OrderAggregateTest()
    { }

    [TestMethod]
    public void Create_order_item_success()
    {
        //Arrange    
        var productId = 1;
        var productName = "FakeProductName";
        var unitPrice = 12;
        var discount = 15;
        var pictureUrl = "FakeUrl";
        var units = 5;

        //Act 
        var fakeOrderItem = new OrderItem(productId, productName, unitPrice, discount, pictureUrl, units);

        //Assert
        Assert.IsNotNull(fakeOrderItem);
    }

    [TestMethod]
    public void Invalid_number_of_units()
    {
        //Arrange    
        var productId = 1;
        var productName = "FakeProductName";
        var unitPrice = 12;
        var discount = 15;
        var pictureUrl = "FakeUrl";
        var units = -1;

        //Act - Assert
        Assert.ThrowsExactly<OrderingDomainException>(() => new OrderItem(productId, productName, unitPrice, discount, pictureUrl, units));
    }

    [TestMethod]
    public void Invalid_total_of_order_item_lower_than_discount_applied()
    {
        //Arrange    
        var productId = 1;
        var productName = "FakeProductName";
        var unitPrice = 12;
        var discount = 15;
        var pictureUrl = "FakeUrl";
        var units = 1;
        
        //Act - Assert
        Assert.ThrowsExactly<OrderingDomainException>(() => new OrderItem(productId, productName, unitPrice, discount, pictureUrl, units));       
    }

    [TestMethod]
    public void Invalid_discount_setting()
    {
        //Arrange    
        var productId = 1;
        var productName = "FakeProductName";
        var unitPrice = 12;
        var discount = 15;
        var pictureUrl = "FakeUrl";
        var units = 5;

        //Act 
        var fakeOrderItem = new OrderItem(productId, productName, unitPrice, discount, pictureUrl, units);

        //Assert
        Assert.ThrowsExactly<OrderingDomainException>(() => fakeOrderItem.SetNewDiscount(-1));
    }

    [TestMethod]
    public void Invalid_units_setting()
    {
        //Arrange    
        var productId = 1;
        var productName = "FakeProductName";
        var unitPrice = 12;
        var discount = 15;
        var pictureUrl = "FakeUrl";
        var units = 5;

        //Act 
        var fakeOrderItem = new OrderItem(productId, productName, unitPrice, discount, pictureUrl, units);

        //Assert
        Assert.ThrowsExactly<OrderingDomainException>(() => fakeOrderItem.AddUnits(-1));
    }

    [TestMethod]
    public void when_add_two_times_on_the_same_item_then_the_total_of_order_should_be_the_sum_of_the_two_items()
    {
        var address = new AddressBuilder().Build();
        var order = new OrderBuilder(address)
            .AddOne(1, "cup", 10.0m, 0, string.Empty)
            .AddOne(1, "cup", 10.0m, 0, string.Empty)
            .Build();

        Assert.AreEqual(20.0m, order.GetTotal());
    }

    [TestMethod]
    public void GetTotalSubtractsDiscountAndAddsShipping()
    {
        var order = BuildPricedOrder();

        Assert.AreEqual(26.99m, order.GetTotal());
    }

    [TestMethod]
    public void ApplyPricingSetsBreakdown()
    {
        var order = BuildPricedOrder();

        Assert.AreEqual(25m, order.Subtotal);
        Assert.AreEqual(3m, order.DiscountTotal);
        Assert.AreEqual(4.99m, order.ShippingFee);
        Assert.AreEqual(26.99m, order.Total);
        Assert.AreEqual(7L, order.PricingSnapshotVersion);
    }

    [TestMethod]
    public void ApplyPricingRejectsNegativeShipping()
    {
        var order = new OrderBuilder(new AddressBuilder().Build())
            .AddOne(1, "cup", 10m, 0, string.Empty)
            .Build();

        Assert.ThrowsExactly<OrderingDomainException>(() => order.ApplyPricing(-0.01m, 1));
    }

    [TestMethod]
    public void AddOrderItemDefaultsBasePriceToUnitPrice()
    {
        var order = new OrderBuilder(new AddressBuilder().Build())
            .AddOne(1, "cup", 10m, 0, string.Empty)
            .AddOne(2, "mug", 8m, 0, string.Empty, basePrice: 9.5m)
            .Build();

        Assert.AreEqual(10m, order.OrderItems.Single(i => i.ProductId == 1).BasePrice);
        Assert.AreEqual(9.5m, order.OrderItems.Single(i => i.ProductId == 2).BasePrice);
    }

    [TestMethod]
    public void AddOrderItemKeepsBreakdownCurrentWithoutApplyPricing()
    {
        var order = new OrderBuilder(new AddressBuilder().Build())
            .AddOne(1, "cup", 10m, 3, string.Empty, units: 2)
            .AddOne(2, "mug", 5m, 0, string.Empty)
            .Build();

        Assert.AreEqual(25m, order.Subtotal);
        Assert.AreEqual(3m, order.DiscountTotal);
        Assert.AreEqual(0m, order.ShippingFee);
        Assert.AreEqual(22m, order.Total);
        Assert.AreEqual(order.GetTotal(), order.Total);
    }

    [TestMethod]
    public void OrderItemRejectsUnitPriceBelowOneCent()
    {
        Assert.ThrowsExactly<OrderingDomainException>(() => new OrderItem(1, "cup", 0.009m, 0, string.Empty, 1));
    }

    private static Order BuildPricedOrder() =>
        new OrderBuilder(new AddressBuilder().Build())
            .AddOne(1, "cup", 10m, 3, string.Empty, units: 2)
            .AddOne(2, "mug", 5m, 0, string.Empty)
            .WithPricing(shippingFee: 4.99m, pricingSnapshotVersion: 7)
            .Build();

    [TestMethod]
    public void Add_new_Order_raises_new_event()
    {
        //Arrange
        var street = "fakeStreet";
        var city = "FakeCity";
        var state = "fakeState";
        var country = "fakeCountry";
        var zipcode = "FakeZipCode";
        var cardTypeId = 5;
        var cardNumber = "12";
        var cardSecurityNumber = "123";
        var cardHolderName = "FakeName";
        var cardExpiration = DateTime.UtcNow.AddYears(1);
        var expectedResult = 1;

        //Act 
        var fakeOrder = new Order("1", "fakeName", new Address(street, city, state, country, zipcode), cardTypeId, cardNumber, cardSecurityNumber, cardHolderName, cardExpiration);

        //Assert
        Assert.HasCount(expectedResult, fakeOrder.DomainEvents);
    }

    [TestMethod]
    public void Add_event_Order_explicitly_raises_new_event()
    {
        //Arrange   
        var street = "fakeStreet";
        var city = "FakeCity";
        var state = "fakeState";
        var country = "fakeCountry";
        var zipcode = "FakeZipCode";
        var cardTypeId = 5;
        var cardNumber = "12";
        var cardSecurityNumber = "123";
        var cardHolderName = "FakeName";
        var cardExpiration = DateTime.UtcNow.AddYears(1);
        var expectedResult = 2;

        //Act 
        var fakeOrder = new Order("1", "fakeName", new Address(street, city, state, country, zipcode), cardTypeId, cardNumber, cardSecurityNumber, cardHolderName, cardExpiration);
        fakeOrder.AddDomainEvent(new OrderStartedDomainEvent(fakeOrder, "fakeName", "1", cardTypeId, cardNumber, cardSecurityNumber, cardHolderName, cardExpiration));
        //Assert
        Assert.HasCount(expectedResult, fakeOrder.DomainEvents);
    }

    [TestMethod]
    public void Remove_event_Order_explicitly()
    {
        //Arrange    
        var street = "fakeStreet";
        var city = "FakeCity";
        var state = "fakeState";
        var country = "fakeCountry";
        var zipcode = "FakeZipCode";
        var cardTypeId = 5;
        var cardNumber = "12";
        var cardSecurityNumber = "123";
        var cardHolderName = "FakeName";
        var cardExpiration = DateTime.UtcNow.AddYears(1);
        var fakeOrder = new Order("1", "fakeName", new Address(street, city, state, country, zipcode), cardTypeId, cardNumber, cardSecurityNumber, cardHolderName, cardExpiration);
        var @fakeEvent = new OrderStartedDomainEvent(fakeOrder, "1", "fakeName", cardTypeId, cardNumber, cardSecurityNumber, cardHolderName, cardExpiration);
        var expectedResult = 1;

        //Act         
        fakeOrder.AddDomainEvent(@fakeEvent);
        fakeOrder.RemoveDomainEvent(@fakeEvent);
        //Assert
        Assert.HasCount(expectedResult, fakeOrder.DomainEvents);
    }
}
