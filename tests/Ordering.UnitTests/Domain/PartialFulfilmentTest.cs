namespace eShop.Ordering.UnitTests.Domain;

using eShop.BusinessRules.Client.Pricing;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

[TestClass]
public class PartialFulfilmentTest
{
    [TestMethod]
    public void PartialFulfilmentMarksRejectedLinesAndRepricesDownward()
    {
        var order = BuildThreeLineOrder();
        order.SetAwaitingValidationStatus();

        order.ApplyStockRejection([2], DiscountAllocation.Allocate);

        Assert.AreEqual(OrderStatus.StockConfirmed, order.OrderStatus);
        Assert.IsTrue(order.OrderItems.Single(i => i.ProductId == 2).IsUnfulfillable);
        Assert.AreEqual(40m, order.Subtotal);
        Assert.AreEqual(6m, order.DiscountTotal);
        Assert.AreEqual(39m, order.Total);
        Assert.AreEqual("These items were out of stock and were removed from your order: P2.", order.Description);
    }

    [TestMethod]
    public void AllOrNothingStockRejectionCancelsOrder()
    {
        var order = new OrderBuilder(new AddressBuilder().Build())
            .AddOne(1, "P1", 10m, 0m, "")
            .AddOne(2, "P2", 20m, 0m, "")
            .Build();
        order.SetAwaitingValidationStatus();

        order.ApplyStockRejection([2], DiscountAllocation.Allocate);

        Assert.AreEqual(OrderStatus.Cancelled, order.OrderStatus);
        Assert.AreEqual("The product items don't have stock: (P2).", order.Description);
        Assert.IsFalse(order.OrderItems.Any(i => i.IsUnfulfillable));
    }

    [TestMethod]
    public void PartialFulfilmentCapsReallocatedOrderDiscountAtRemainingLineTotals()
    {
        var order = new OrderBuilder(new AddressBuilder().Build())
            .AddOne(1, "P1", 5m, 3m, "", orderDiscountShare: 3m)
            .AddOne(2, "P2", 20m, 12m, "", orderDiscountShare: 12m)
            .WithPricing(2m, 1)
            .WithFulfilmentRules(TimeSpan.FromMinutes(1), StockShortfallPolicy.PartialFulfilment)
            .Build();
        order.SetAwaitingValidationStatus();

        order.ApplyStockRejection([2], DiscountAllocation.Allocate);

        Assert.AreEqual(5m, order.DiscountTotal);
        Assert.AreEqual(5m, order.OrderItems.Single(i => i.ProductId == 1).Discount);
        Assert.AreEqual(2m, order.Total);
    }

    [TestMethod]
    public void PartialFulfilmentWithEveryLineRejectedCancels()
    {
        var order = BuildThreeLineOrder();
        order.SetAwaitingValidationStatus();

        order.ApplyStockRejection([1, 2, 3], DiscountAllocation.Allocate);

        Assert.AreEqual(OrderStatus.Cancelled, order.OrderStatus);
        Assert.IsFalse(order.OrderItems.Any(i => i.IsUnfulfillable));
    }

    [TestMethod]
    public void StockRejectionAfterPartialFulfilmentIsIgnored()
    {
        var order = BuildThreeLineOrder();
        order.SetAwaitingValidationStatus();
        order.ApplyStockRejection([2], DiscountAllocation.Allocate);

        order.ApplyStockRejection([1], DiscountAllocation.Allocate);

        Assert.AreEqual(OrderStatus.StockConfirmed, order.OrderStatus);
        Assert.IsFalse(order.OrderItems.Single(i => i.ProductId == 1).IsUnfulfillable);
        Assert.AreEqual(40m, order.Subtotal);
        Assert.AreEqual(39m, order.Total);
        Assert.AreEqual("These items were out of stock and were removed from your order: P2.", order.Description);
    }

    [TestMethod]
    public void PartialFulfilmentIgnoresRejectedIdsNotOnOrder()
    {
        var order = BuildThreeLineOrder();
        order.SetAwaitingValidationStatus();

        order.ApplyStockRejection([99], DiscountAllocation.Allocate);

        Assert.AreEqual(OrderStatus.StockConfirmed, order.OrderStatus);
        Assert.IsFalse(order.OrderItems.Any(i => i.IsUnfulfillable));
        Assert.AreEqual(59m, order.Total);
    }

    [TestMethod]
    public void PaidDomainEventExcludesUnfulfillableLines()
    {
        var order = BuildThreeLineOrder();
        order.SetAwaitingValidationStatus();
        order.ApplyStockRejection([2], DiscountAllocation.Allocate);

        order.SetPaidStatus();

        var paid = order.DomainEvents!.OfType<OrderStatusChangedToPaidDomainEvent>().Single();
        CollectionAssert.AreEqual(new[] { 1, 3 }, paid.OrderItems.Select(i => i.ProductId).ToArray());
    }

    [TestMethod]
    public void GetTotalExcludesUnfulfillableLines()
    {
        var order = BuildThreeLineOrder();
        order.SetAwaitingValidationStatus();
        order.ApplyStockRejection([2], DiscountAllocation.Allocate);

        Assert.AreEqual(39m, order.GetTotal());
    }

    // Lines 10, 20, 30 with order-discount shares 1, 2, 3; shipping 5; total 59.
    private static Order BuildThreeLineOrder() =>
        new OrderBuilder(new AddressBuilder().Build())
            .AddOne(1, "P1", 10m, 1m, "", orderDiscountShare: 1m)
            .AddOne(2, "P2", 20m, 2m, "", orderDiscountShare: 2m)
            .AddOne(3, "P3", 30m, 3m, "", orderDiscountShare: 3m)
            .WithPricing(5m, 1)
            .WithFulfilmentRules(TimeSpan.FromMinutes(1), StockShortfallPolicy.PartialFulfilment)
            .Build();
}
