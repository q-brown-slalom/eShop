using System.Text.Json.Serialization;
using eShop.BusinessRules.Client.Rules.Types;
using static eShop.BusinessRules.UnitTests.SnapshotFactory;

namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class PricingCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions PromoJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly RuleTarget AllTarget = new(RuleTargetKind.All, null);

    private static string Promo(LinePromotion[]? line = null, OrderPromotion[]? order = null)
        => JsonSerializer.Serialize(new PromotionParameters(line ?? [], order ?? []), PromoJson);

    private static OrderPromotion Amount(decimal value, decimal min, string label) => new(DiscountKind.AmountOff, value, min, label);

    private static OrderPromotion Pct(decimal value, decimal min, string label) => new(DiscountKind.PercentOff, value, min, label);

    private static LinePromotion LinePct(decimal value, string label, RuleTarget? target = null, int? minUnits = null)
        => new(target ?? AllTarget, DiscountKind.PercentOff, value, minUnits, label);

    private static LinePromotion LineAmount(decimal value, string label, RuleTarget? target = null, int? minUnits = null)
        => new(target ?? AllTarget, DiscountKind.AmountOff, value, minUnits, label);

    private static RulesSnapshot Shipping(decimal fee, decimal? threshold)
        => Snapshot(Rule("SHP-001", JsonSerializer.Serialize(new ShippingFeeParameters(fee, threshold), PromoJson)));

    private static Dictionary<int, CatalogPrice> Catalog(params (int ProductId, decimal Price)[] items)
        => Catalog([.. items.Select(i => (i.ProductId, i.Price, TypeId: 1, BrandId: 1))]);

    private static Dictionary<int, CatalogPrice> Catalog(params (int ProductId, decimal Price, int TypeId, int BrandId)[] items)
        => items.ToDictionary(
            i => i.ProductId,
            i => new CatalogPrice(i.ProductId, $"Product {i.ProductId}", i.Price, i.TypeId, i.BrandId, PictureUrl: null));

    private static PriceQuote Calc(QuoteLine[] lines, IReadOnlyDictionary<int, CatalogPrice> catalog, RulesSnapshot? snapshot = null, DateTimeOffset? now = null)
        => new PricingCalculator().Calculate(lines, catalog, snapshot ?? RulesSnapshot.Empty, now ?? Now);

    private static RulesSnapshot PriceRules(string entriesJson, int fromHours = -1)
        => SnapshotFactory.Snapshot(SnapshotFactory.Rule("PRC-001", $$"""{"entries":{{entriesJson}}}""", fromHours: fromHours));

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

    [TestMethod]
    public void PriceAdjustmentPercentOffByTypeReducesUnitPrice()
    {
        var snapshot = PriceRules("""[{"target":{"kind":"Type","id":2},"mode":"PercentOff","value":10}]""");

        var q = Calc([new(1, 1)], Catalog((1, 79.99m, 2, 1)), snapshot);

        Assert.AreEqual(79.99m, q.Lines[0].BasePrice);
        Assert.AreEqual(71.99m, q.Lines[0].UnitPrice);
        Assert.AreEqual(71.99m, q.Total);
    }

    [TestMethod]
    public void PriceAdjustmentMostSpecificTargetWins()
    {
        var itemOverAll = PriceRules("""
            [{"target":{"kind":"All"},"mode":"PercentOff","value":10},
             {"target":{"kind":"Item","id":1},"mode":"FixedPrice","value":5.00}]
            """);
        var typeOverBrand = PriceRules("""
            [{"target":{"kind":"Brand","id":3},"mode":"PercentOff","value":20},
             {"target":{"kind":"Type","id":2},"mode":"PercentOff","value":10}]
            """);

        Assert.AreEqual(5.00m, Calc([new(1, 1)], Catalog((1, 100m, 2, 3)), itemOverAll).Lines[0].UnitPrice);
        Assert.AreEqual(90.00m, Calc([new(1, 1)], Catalog((1, 100m, 2, 3)), typeOverBrand).Lines[0].UnitPrice);
    }

    [TestMethod]
    public void PriceAdjustmentNeverGoesBelowOneCent()
    {
        var snapshot = PriceRules("""[{"target":{"kind":"All"},"mode":"PercentOff","value":90}]""");

        var q = Calc([new(1, 1)], Catalog((1, 0.01m)), snapshot);

        Assert.AreEqual(0.01m, q.Lines[0].UnitPrice);
    }

    [TestMethod]
    public void UnitPriceIsRoundedHalfAwayFromZero()
    {
        var snapshot = PriceRules("""[{"target":{"kind":"All"},"mode":"PercentOff","value":50}]""");

        var q = Calc([new(1, 1)], Catalog((1, 10.05m)), snapshot);

        Assert.AreEqual(5.03m, q.Lines[0].UnitPrice);
    }

    [TestMethod]
    public void PriceAdjustmentTakesEffectAtEffectiveFrom()
    {
        var snapshot = PriceRules("""[{"target":{"kind":"All"},"mode":"PercentOff","value":50}]""", fromHours: 1);

        Assert.AreEqual(10m, Calc([new(1, 1)], Catalog((1, 10m)), snapshot).Lines[0].UnitPrice);
        Assert.AreEqual(5m, Calc([new(1, 1)], Catalog((1, 10m)), snapshot, Now.AddHours(1)).Lines[0].UnitPrice);
    }

    [TestMethod]
    public void UnparseableParametersFallBackToDefault()
    {
        var snapshot = SnapshotFactory.Snapshot(SnapshotFactory.Rule("PRC-001", """{"entries":"oops"}"""));

        var q = Calc([new(1, 2)], Catalog((1, 10m)), snapshot);

        Assert.AreEqual(10m, q.Lines[0].UnitPrice);
        Assert.AreEqual(20m, q.Total);
    }

    private static RulesSnapshot Promotions(LinePromotion[]? line = null, OrderPromotion[]? order = null)
        => Snapshot(Rule("PRM-001", Promo(line, order)));

    [TestMethod]
    public void LinePercentPromotionSetsDiscountAndLabel()
    {
        var q = Calc([new(1, 2)], Catalog((1, 50m)), Promotions([LinePct(10m, "10% off")]));

        Assert.AreEqual(10.00m, q.Lines[0].Discount);
        Assert.AreEqual("10% off", q.Lines[0].PromotionLabel);
        Assert.AreEqual(0m, q.Lines[0].OrderDiscountShare);
        Assert.AreEqual(90.00m, q.Total);
    }

    [TestMethod]
    public void LineAmountOffIsPerUnit()
    {
        var q = Calc([new(1, 3)], Catalog((1, 20m)), Promotions([LineAmount(5m, "5 off")]));

        Assert.AreEqual(15.00m, q.Lines[0].Discount);
        Assert.AreEqual(45.00m, q.Total);
    }

    [TestMethod]
    public void LineDiscountIsRoundedHalfAwayFromZero()
    {
        var q = Calc([new(1, 1)], Catalog((1, 10.05m)), Promotions([LinePct(50m, "Half")]));

        Assert.AreEqual(5.03m, q.Lines[0].Discount);
    }

    [TestMethod]
    public void LineAmountOffIsCappedAtLineTotal()
    {
        var q = Calc([new(1, 1)], Catalog((1, 10m)), Promotions([LineAmount(50m, "Huge")]));

        Assert.AreEqual(10.00m, q.Lines[0].Discount);
        Assert.AreEqual(0m, q.Total);
    }

    [TestMethod]
    public void OrderAmountOffIsCappedAtSubtotal()
    {
        var q = Calc([new(1, 1)], Catalog((1, 12m)), Promotions(order: [Amount(20m, min: 0m, "Huge")]));

        Assert.AreEqual(12.00m, q.DiscountTotal);
        Assert.AreEqual(12.00m, q.OrderDiscount);
        Assert.AreEqual(0m, q.Total);
    }

    [TestMethod]
    public void LinePromotionMinUnitsNotMetFallsBackToLessSpecificEntry()
    {
        var snapshot = Promotions([
            LinePct(15m, "Bulk", new RuleTarget(RuleTargetKind.Item, 1), minUnits: 3),
            LinePct(5m, "Everything"),
        ]);

        var q = Calc([new(1, 2)], Catalog((1, 100m)), snapshot);

        Assert.AreEqual(10.00m, q.Lines[0].Discount);
        Assert.AreEqual("Everything", q.Lines[0].PromotionLabel);
    }

    [TestMethod]
    public void LinePromotionMostSpecificApplicableWinsThenHighestDiscount()
    {
        var item = new RuleTarget(RuleTargetKind.Item, 1);
        var specificOverBigger = Promotions([LinePct(20m, "All 20"), LinePct(5m, "Item 5", item)]);
        var tiers = Promotions([LinePct(5m, "Tier 1", item, minUnits: 1), LinePct(15m, "Tier 3", item, minUnits: 3)]);

        var first = Calc([new(1, 1)], Catalog((1, 100m)), specificOverBigger);
        var second = Calc([new(1, 3)], Catalog((1, 100m)), tiers);

        Assert.AreEqual(5.00m, first.Lines[0].Discount);
        Assert.AreEqual("Item 5", first.Lines[0].PromotionLabel);
        Assert.AreEqual(45.00m, second.Lines[0].Discount);
        Assert.AreEqual("Tier 3", second.Lines[0].PromotionLabel);
    }

    [TestMethod]
    public void LinePromotionTieGoesToFirstListed()
    {
        var q = Calc([new(1, 1)], Catalog((1, 100m)), Promotions([LinePct(10m, "First"), LineAmount(10m, "Second")]));

        Assert.AreEqual("First", q.Lines[0].PromotionLabel);
    }

    [TestMethod]
    public void LinePromotionAppliesAfterPriceAdjustment()
    {
        var snapshot = Snapshot(
            Rule("PRC-001", """{"entries":[{"target":{"kind":"All"},"mode":"PercentOff","value":10}]}"""),
            Rule("PRM-001", Promo([LinePct(10m, "10% off")])));

        var q = Calc([new(1, 1)], Catalog((1, 100m)), snapshot);

        Assert.AreEqual(90.00m, q.Lines[0].UnitPrice);
        Assert.AreEqual(9.00m, q.Lines[0].Discount);
        Assert.AreEqual(81.00m, q.Total);
    }

    [TestMethod]
    public void BestOrderPromotionIsAllocatedProportionallyInCents()
    {   // lines 30.00 + 10.00; order promotions: 10.00 off at ≥ 40, 5 % off at ≥ 0 → 10.00 wins
        var q = Calc([new(1, 1), new(2, 1)], Catalog((1, 30m), (2, 10m)), Snapshot(Rule("PRM-001", Promo(order: [Amount(10m, min: 40m, "Spend 40 save 10"), Pct(5m, min: 0m, "5%")]))));

        Assert.AreEqual(7.50m, q.Lines[0].OrderDiscountShare);
        Assert.AreEqual(2.50m, q.Lines[1].OrderDiscountShare);
        Assert.AreEqual(7.50m, q.Lines[0].Discount);
        Assert.AreEqual(10.00m, q.OrderDiscount);
        Assert.AreEqual(30.00m, q.Total);
        Assert.AreEqual("Spend 40 save 10", q.OrderPromotionLabel);
    }

    [TestMethod]
    public void OrderPromotionTieGoesToFirstListed()
    {
        var q = Calc([new(1, 1)], Catalog((1, 100m)), Promotions(order: [Amount(10m, min: 0m, "First"), Pct(10m, min: 0m, "Second")]));

        Assert.AreEqual("First", q.OrderPromotionLabel);
    }

    [TestMethod]
    public void OrderDiscountAllocationKeepsEveryLineWithinItsTotal()
    {   // 3 × 0.33, 1 × 0.01, 2 × 0.05; 100 % order discount
        var q = Calc([new(1, 3), new(2, 1), new(3, 2)], Catalog((1, 0.33m), (2, 0.01m), (3, 0.05m)), Snapshot(Rule("PRM-001", Promo(order: [Pct(100m, min: 0m, "Free")]))));

        Assert.IsTrue(q.Lines.All(l => l.Discount <= l.Units * l.UnitPrice));
        Assert.AreEqual(0m, q.Total);
    }

    [TestMethod]
    public void OrderPromotionAddsToLinePromotionDiscount()
    {
        var q = Calc([new(1, 1)], Catalog((1, 100m)), Promotions([LinePct(10m, "Line")], [Pct(10m, min: 0m, "Order")]));

        Assert.AreEqual(9.00m, q.Lines[0].OrderDiscountShare);
        Assert.AreEqual(19.00m, q.Lines[0].Discount);
        Assert.AreEqual("Line", q.Lines[0].PromotionLabel);
        Assert.AreEqual("Order", q.OrderPromotionLabel);
        Assert.AreEqual(81.00m, q.Total);
    }

    [TestMethod]
    public void OrderPromotionThresholdUsesSubtotalAfterLinePromotions()
    {
        var snapshot = Promotions([LinePct(10m, "Line")], [Amount(10m, min: 50m, "Spend 50")]);

        var q = Calc([new(1, 1)], Catalog((1, 50m)), snapshot);

        Assert.AreEqual(5.00m, q.DiscountTotal);
        Assert.AreEqual(0m, q.OrderDiscount);
        Assert.IsNull(q.OrderPromotionLabel);
    }

    [TestMethod]
    public void ShippingFeeAppliesBelowThresholdAndIsFreeAtThreshold()
    {
        var snapshot = Shipping(4.99m, 50m);

        var below = Calc([new(1, 1)], Catalog((1, 49.99m)), snapshot);
        var atThreshold = Calc([new(1, 1)], Catalog((1, 50.00m)), snapshot);

        Assert.AreEqual(4.99m, below.ShippingFee);
        Assert.AreEqual(54.98m, below.Total);
        Assert.AreEqual(0m, atThreshold.ShippingFee);
        Assert.AreEqual(50.00m, atThreshold.Total);
    }

    [TestMethod]
    public void ShippingThresholdUsesDiscountedSubtotal()
    {
        var snapshot = Snapshot(
            Rule("SHP-001", """{"fee":4.99,"freeShippingThreshold":50}"""),
            Rule("PRM-001", Promo([LineAmount(10m, "10 off")])));

        Assert.AreEqual(0m, Calc([new(1, 1)], Catalog((1, 60m)), snapshot).ShippingFee);
        Assert.AreEqual(4.99m, Calc([new(1, 1)], Catalog((1, 59.99m)), snapshot).ShippingFee);
    }

    [TestMethod]
    public void ShippingWithoutThresholdIsAlwaysCharged()
    {
        var q = Calc([new(1, 10)], Catalog((1, 100m)), Shipping(4.99m, null));

        Assert.AreEqual(4.99m, q.ShippingFee);
        Assert.AreEqual(1004.99m, q.Total);
    }

    [TestMethod]
    public void PromotionRulesWithInvalidParametersFallBackToNoDiscount()
    {
        var q = Calc([new(1, 1)], Catalog((1, 10m)), Snapshot(Rule("PRM-001", """{"linePromotions":"oops"}""")));

        Assert.AreEqual(0m, q.DiscountTotal);
        Assert.AreEqual(10m, q.Total);
    }
}
