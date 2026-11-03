namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class PricingCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static Dictionary<int, CatalogPrice> Catalog(params (int ProductId, decimal Price)[] items)
        => items.ToDictionary(
            i => i.ProductId,
            i => new CatalogPrice(i.ProductId, $"Product {i.ProductId}", i.Price, CatalogTypeId: 1, CatalogBrandId: 1, PictureUrl: null));

    private static PriceQuote Calc(QuoteLine[] lines, IReadOnlyDictionary<int, CatalogPrice> catalog, RulesSnapshot? snapshot = null)
        => new PricingCalculator().Calculate(lines, catalog, snapshot ?? RulesSnapshot.Empty, Now);

    [TestMethod]
    public void CalculateWithEmptySnapshotChargesCatalogPrice()
    {
        var q = Calc([new(1, 2), new(2, 1)], Catalog((1, 19.50m), (2, 8.00m)));

        Assert.AreEqual(47.00m, q.Subtotal);
        Assert.AreEqual(0m, q.DiscountTotal);
        Assert.AreEqual(0m, q.ShippingFee);
        Assert.AreEqual(47.00m, q.Total);
        Assert.AreEqual(0L, q.SnapshotVersion);
        Assert.AreEqual(19.50m, q.Lines[0].BasePrice);
        Assert.AreEqual(19.50m, q.Lines[0].UnitPrice);
        Assert.AreEqual(39.00m, q.Lines[0].LineTotal);
        Assert.IsNull(q.Lines[0].PromotionLabel);
    }

    [TestMethod]
    public void CalculateReportsSnapshotVersion()
    {
        var q = Calc([new(1, 1)], Catalog((1, 5m)), new RulesSnapshot(42, Now, []));

        Assert.AreEqual(42L, q.SnapshotVersion);
    }

    [TestMethod]
    public void CalculateMergesDuplicateProductLines()
    {
        var q = Calc([new(7, 1), new(7, 2)], Catalog((7, 10m)));

        Assert.HasCount(1, q.Lines);
        Assert.AreEqual(3, q.Lines[0].Units);
        Assert.AreEqual(30m, q.Total);
    }

    [TestMethod]
    public void CalculateKeepsOrderOfFirstAppearance()
    {
        var q = Calc([new(3, 1), new(1, 1), new(3, 1)], Catalog((1, 1m), (3, 3m)));

        CollectionAssert.AreEqual(new[] { 3, 1 }, q.Lines.Select(l => l.ProductId).ToArray());
    }

    [TestMethod]
    public void CalculateThrowsForUnknownProducts()
    {
        var ex = Assert.ThrowsExactly<UnknownProductsException>(() => Calc([new(1, 1), new(99, 1)], Catalog((1, 5m))));

        CollectionAssert.AreEqual(new[] { 99 }, ex.ProductIds.ToArray());
    }

    [TestMethod]
    public void CalculateReportsAllUnknownProductsSortedAndDistinct()
    {
        var ex = Assert.ThrowsExactly<UnknownProductsException>(() => Calc([new(42, 1), new(1, 1), new(7, 1), new(42, 1)], Catalog((1, 5m))));

        CollectionAssert.AreEqual(new[] { 7, 42 }, ex.ProductIds.ToArray());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void CalculateRejectsNonPositiveUnits(int units)
        => Assert.ThrowsExactly<ArgumentException>(() => Calc([new(1, units)], Catalog((1, 5m))));

    [TestMethod]
    public void CalculateRejectsEmptyLines()
        => Assert.ThrowsExactly<ArgumentException>(() => Calc([], Catalog((1, 5m))));

    [TestMethod]
    [DataRow(1.005, 1.01)]
    [DataRow(2.345, 2.35)]
    [DataRow(-0.005, -0.01)]
    public void RoundIsHalfAwayFromZero(double input, double expected)
        => Assert.AreEqual((decimal)expected, PriceRounding.Round((decimal)input));
}
