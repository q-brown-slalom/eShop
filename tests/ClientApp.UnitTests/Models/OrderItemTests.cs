using System.Text.Json;
using eShop.ClientApp.Models.Orders;

namespace ClientApp.UnitTests.Models;

[TestClass]
public class OrderItemTests
{
    [TestMethod]
    public void OrderItemDeserializesUnfulfillableFlag()
    {
        var item = JsonSerializer.Deserialize<OrderItem>("""{"isUnfulfillable":true}""");

        Assert.IsNotNull(item);
        Assert.IsTrue(item.IsUnfulfillable);
    }

    [TestMethod]
    public void UnfulfillableItemTotalIsZero()
    {
        var item = new OrderItem { Quantity = 3, UnitPrice = 10m, Discount = 5m, IsUnfulfillable = true };

        Assert.AreEqual(0m, item.Total);
    }

    [TestMethod]
    public void ItemTotalSubtractsDiscount()
    {
        var item = new OrderItem { Quantity = 3, UnitPrice = 10m, Discount = 5m };

        Assert.AreEqual(25m, item.Total);
    }
}
