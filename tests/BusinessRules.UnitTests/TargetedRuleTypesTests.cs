using eShop.BusinessRules.Client.Rules.Types;
using Microsoft.Extensions.DependencyInjection;
using static eShop.BusinessRules.UnitTests.SnapshotFactory;

namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class TargetedRuleTypesTests
{
    private static readonly CatalogPrice Product = new(99, "Boot", 10m, CatalogTypeId: 1, CatalogBrandId: 2, PictureUrl: null);

    [TestMethod]
    [DataRow(RuleTargetKind.All, null, true)]
    [DataRow(RuleTargetKind.Brand, 2, true)]
    [DataRow(RuleTargetKind.Brand, 1, false)]
    [DataRow(RuleTargetKind.Type, 1, true)]
    [DataRow(RuleTargetKind.Type, 2, false)]
    [DataRow(RuleTargetKind.Item, 99, true)]
    [DataRow(RuleTargetKind.Item, 2, false)]
    public void TargetMatchesByKind(RuleTargetKind kind, int? id, bool expected)
        => Assert.AreEqual(expected, new RuleTarget(kind, id).Matches(Product));

    [TestMethod]
    public void SpecificityFollowsKindOrder()
    {
        Assert.IsLessThan(new RuleTarget(RuleTargetKind.Item, 1).Specificity, new RuleTarget(RuleTargetKind.Type, 1).Specificity);
        Assert.IsLessThan(new RuleTarget(RuleTargetKind.Type, 1).Specificity, new RuleTarget(RuleTargetKind.Brand, 1).Specificity);
        Assert.IsLessThan(new RuleTarget(RuleTargetKind.Brand, 1).Specificity, new RuleTarget(RuleTargetKind.All, null).Specificity);
    }

    [TestMethod]
    public void TargetParsesFromJsonObject()
    {
        Assert.IsTrue(new PriceAdjustmentRuleType().TryParse(J("""{"entries":[{"target":{"kind":"Type","id":1},"mode":"PercentOff","value":10}]}"""), out var parsed));
        Assert.AreEqual(new RuleTarget(RuleTargetKind.Type, 1), parsed.Entries.Single().Target);
    }

    [TestMethod]
    public void PriceAdjustmentDefaultIsEmpty()
    {
        var type = new PriceAdjustmentRuleType();

        Assert.AreEqual("PRC-001", type.Id);
        Assert.IsTrue(type.AffectsPrice);
        Assert.AreEqual(RuleTargeting.Targeted, type.Targeting);
        Assert.IsEmpty(type.Resolve(RulesSnapshot.Empty, Now).Entries);
        Assert.IsEmpty(type.Validate(type.Default));
    }

    [TestMethod]
    public void PriceAdjustmentPercentOffAbove90IsInvalid()
    {
        var errors = new PriceAdjustmentRuleType().Validate(J(Entries(("""{"kind":"All"}""", "PercentOff", "10"), ("""{"kind":"All"}""", "PercentOff", "90.01"))));

        Assert.Contains("entries[1].value: percent off must be between 0 and 90.", errors);
    }

    [TestMethod]
    public void PriceAdjustmentPercentOffOf90IsValid()
        => Assert.IsEmpty(new PriceAdjustmentRuleType().Validate(J(Entries(("""{"kind":"All"}""", "PercentOff", "90")))));

    [TestMethod]
    public void PriceAdjustmentFixedPriceZeroIsInvalid()
        => Assert.IsNotEmpty(new PriceAdjustmentRuleType().Validate(J(Entries(("""{"kind":"Item","id":5}""", "FixedPrice", "0")))));

    [TestMethod]
    public void PriceAdjustmentFixedPriceWithThreeDecimalsIsInvalid()
        => Assert.IsNotEmpty(new PriceAdjustmentRuleType().Validate(J(Entries(("""{"kind":"Item","id":5}""", "FixedPrice", "9.999")))));

    [TestMethod]
    public void PriceAdjustmentFixedPriceIsCappedAtOneMillion()
    {
        var type = new PriceAdjustmentRuleType();

        Assert.IsEmpty(type.Validate(J(Entries(("""{"kind":"Item","id":5}""", "FixedPrice", "1000000")))));
        Assert.Contains(
            "entries[0].value: fixed price must be between 0.01 and 1000000.",
            type.Validate(J(Entries(("""{"kind":"Item","id":5}""", "FixedPrice", "1000000.01")))));
        Assert.IsNotEmpty(type.Validate(J(Entries(("""{"kind":"Item","id":5}""", "FixedPrice", "79228162514264337593543950335")))));
    }

    [TestMethod]
    public void PromotionAmountOffIsCappedAtOneMillion()
    {
        var type = new PromotionRuleType();

        Assert.IsEmpty(type.Validate(J(Line("""{"kind":"All"}""", "AmountOff", "1000000", "null", "Sale"))));
        Assert.Contains(
            "linePromotions[0].value: amount off must be between 0.01 and 1000000.",
            type.Validate(J(Line("""{"kind":"All"}""", "AmountOff", "1000000.01", "null", "Sale"))));
        Assert.IsEmpty(type.Validate(J(Order("AmountOff", "1000000", "0", "Sale"))));
        Assert.Contains(
            "orderPromotions[0].value: amount off must be between 0.01 and 1000000.",
            type.Validate(J(Order("AmountOff", "1000000.01", "0", "Sale"))));
    }

    [TestMethod]
    public void PromotionMinSubtotalIsCappedAtOneMillion()
    {
        var type = new PromotionRuleType();

        Assert.IsEmpty(type.Validate(J(Order("AmountOff", "5", "1000000", "Sale"))));
        Assert.Contains(
            "orderPromotions[0].minSubtotal: must be between 0 and 1000000.",
            type.Validate(J(Order("AmountOff", "5", "1000000.01", "Sale"))));
    }

    [TestMethod]
    public void PriceAdjustmentDuplicateTargetIsInvalid()
    {
        var errors = new PriceAdjustmentRuleType().Validate(J(Entries(("""{"kind":"Type","id":1}""", "PercentOff", "10"), ("""{"kind":"Type","id":1}""", "FixedPrice", "5"))));

        Assert.HasCount(1, errors);
        Assert.StartsWith("entries[1].target:", errors[0]);
    }

    [TestMethod]
    public void PriceAdjustmentSameIdDifferentKindIsValid()
        => Assert.IsEmpty(new PriceAdjustmentRuleType().Validate(J(Entries(("""{"kind":"Type","id":1}""", "PercentOff", "10"), ("""{"kind":"Brand","id":1}""", "PercentOff", "10")))));

    [TestMethod]
    public void PriceAdjustmentMoreThan500EntriesIsInvalid()
    {
        var entries = Enumerable.Range(1, 501).Select(i => ($$"""{"kind":"Item","id":{{i}}}""", "PercentOff", "10")).ToArray();

        Assert.IsNotEmpty(new PriceAdjustmentRuleType().Validate(J(Entries(entries))));
        Assert.IsEmpty(new PriceAdjustmentRuleType().Validate(J(Entries(entries[..500]))));
    }

    [TestMethod]
    public void PriceAdjustmentMissingEntriesIsInvalid()
        => Assert.IsNotEmpty(new PriceAdjustmentRuleType().Validate(J("{}")));

    [TestMethod]
    public void PriceAdjustmentMissingTargetIsInvalid()
        => Assert.IsNotEmpty(new PriceAdjustmentRuleType().Validate(J("""{"entries":[{"mode":"PercentOff","value":10}]}""")));

    [TestMethod]
    public void PriceAdjustmentIntegerModeIsInvalid()
        => Assert.IsNotEmpty(new PriceAdjustmentRuleType().Validate(J("""{"entries":[{"target":{"kind":"All"},"mode":0,"value":10}]}""")));

    [TestMethod]
    public void TargetAllWithIdIsInvalid()
        => Assert.IsNotEmpty(new PriceAdjustmentRuleType().Validate(J(Entries(("""{"kind":"All","id":1}""", "PercentOff", "10")))));

    [TestMethod]
    [DataRow("""{"kind":"Item"}""")]
    [DataRow("""{"kind":"Item","id":0}""")]
    [DataRow("""{"kind":"Brand","id":-3}""")]
    public void TargetItemWithoutIdIsInvalid(string target)
        => Assert.IsNotEmpty(new PriceAdjustmentRuleType().Validate(J(Entries((target, "PercentOff", "10")))));

    [TestMethod]
    public void TargetWithUnknownKindOrMemberIsInvalid()
    {
        Assert.IsNotEmpty(new PriceAdjustmentRuleType().Validate(J(Entries(("""{"kind":"Category","id":1}""", "PercentOff", "10")))));
        Assert.IsNotEmpty(new PriceAdjustmentRuleType().Validate(J(Entries(("""{"kind":"Item","id":1,"extra":1}""", "PercentOff", "10")))));
    }

    [TestMethod]
    public void PromotionDefaultIsEmpty()
    {
        var type = new PromotionRuleType();
        var value = type.Resolve(RulesSnapshot.Empty, Now);

        Assert.AreEqual("PRM-001", type.Id);
        Assert.IsTrue(type.AffectsPrice);
        Assert.AreEqual(RuleTargeting.Targeted, type.Targeting);
        Assert.IsEmpty(value.LinePromotions);
        Assert.IsEmpty(value.OrderPromotions);
        Assert.IsEmpty(type.Validate(type.Default));
    }

    [TestMethod]
    public void PromotionLabelLongerThan60IsInvalid()
    {
        Assert.IsNotEmpty(new PromotionRuleType().Validate(J(Line("""{"kind":"All"}""", "PercentOff", "10", "null", new string('a', 61)))));
        Assert.IsEmpty(new PromotionRuleType().Validate(J(Line("""{"kind":"All"}""", "PercentOff", "10", "null", new string('a', 60)))));
    }

    [TestMethod]
    public void PromotionLabelLengthIsMeasuredOnTheRawString()
    {
        Assert.IsNotEmpty(new PromotionRuleType().Validate(J(Line("""{"kind":"All"}""", "PercentOff", "10", "null", " " + new string('a', 60)))));
        Assert.IsNotEmpty(new PromotionRuleType().Validate(J(Line("""{"kind":"All"}""", "PercentOff", "10", "null", new string('a', 60) + " "))));
        Assert.IsEmpty(new PromotionRuleType().Validate(J(Line("""{"kind":"All"}""", "PercentOff", "10", "null", " " + new string('a', 59)))));
    }

    [TestMethod]
    [DataRow("<b>Sale</b>")]
    [DataRow("Sale>")]
    [DataRow("   ")]
    [DataRow("")]
    public void PromotionLabelWithHtmlOrBlankIsInvalid(string label)
        => Assert.IsNotEmpty(new PromotionRuleType().Validate(J(Line("""{"kind":"All"}""", "PercentOff", "10", "null", label))));

    [TestMethod]
    public void PromotionOrderLabelWithHtmlIsInvalid()
    {
        var errors = new PromotionRuleType().Validate(J("""{"linePromotions":[],"orderPromotions":[{"kind":"AmountOff","value":5,"minSubtotal":0,"label":"<b>Sale</b>"}]}"""));

        Assert.HasCount(1, errors);
        Assert.StartsWith("orderPromotions[0].label:", errors[0]);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("100.01")]
    [DataRow("-5")]
    public void PromotionPercentOffOutsideRangeIsInvalid(string value)
        => Assert.IsNotEmpty(new PromotionRuleType().Validate(J(Line("""{"kind":"All"}""", "PercentOff", value, "null", "Sale"))));

    [TestMethod]
    public void PromotionPercentOffOf100IsValid()
        => Assert.IsEmpty(new PromotionRuleType().Validate(J(Line("""{"kind":"All"}""", "PercentOff", "100", "null", "Free"))));

    [TestMethod]
    [DataRow("0")]
    [DataRow("1.005")]
    public void PromotionAmountOffMustBePositiveWithTwoDecimals(string value)
        => Assert.IsNotEmpty(new PromotionRuleType().Validate(J(Line("""{"kind":"All"}""", "AmountOff", value, "null", "Sale"))));

    [TestMethod]
    public void PromotionMinUnitsZeroIsInvalid()
    {
        Assert.IsNotEmpty(new PromotionRuleType().Validate(J(Line("""{"kind":"All"}""", "PercentOff", "10", "0", "Sale"))));
        Assert.IsEmpty(new PromotionRuleType().Validate(J(Line("""{"kind":"All"}""", "PercentOff", "10", "1", "Sale"))));
    }

    [TestMethod]
    public void PromotionNegativeOrFractionalMinSubtotalIsInvalid()
    {
        Assert.IsNotEmpty(new PromotionRuleType().Validate(J(Order("AmountOff", "5", "-0.01", "Sale"))));
        Assert.IsNotEmpty(new PromotionRuleType().Validate(J(Order("AmountOff", "5", "10.001", "Sale"))));
        Assert.IsEmpty(new PromotionRuleType().Validate(J(Order("AmountOff", "5", "0", "Sale"))));
    }

    [TestMethod]
    public void PromotionMissingOrderPromotionsIsInvalid()
        => Assert.IsNotEmpty(new PromotionRuleType().Validate(J("""{"linePromotions":[]}""")));

    [TestMethod]
    public void PromotionMissingLinePromotionsIsInvalid()
        => Assert.IsNotEmpty(new PromotionRuleType().Validate(J("""{"orderPromotions":[]}""")));

    [TestMethod]
    public void PromotionMoreThan500LinePromotionsIsInvalid()
    {
        var line = """{"target":{"kind":"All"},"kind":"PercentOff","value":10,"minUnits":null,"label":"Sale"}""";
        var json = $$"""{"linePromotions":[{{string.Join(',', Enumerable.Repeat(line, 501))}}],"orderPromotions":[]}""";

        Assert.IsNotEmpty(new PromotionRuleType().Validate(J(json)));
    }

    [TestMethod]
    public void PromotionDuplicateTargetsAreValid()
    {
        var json = """
            {"linePromotions":[
              {"target":{"kind":"Item","id":7},"kind":"PercentOff","value":10,"minUnits":2,"label":"10% off 2+"},
              {"target":{"kind":"Item","id":7},"kind":"PercentOff","value":15,"minUnits":3,"label":"15% off 3+"}],
             "orderPromotions":[]}
            """;

        Assert.IsEmpty(new PromotionRuleType().Validate(J(json)));
    }

    [TestMethod]
    public void PromotionErrorsCarryJsonPath()
    {
        var errors = new PromotionRuleType().Validate(J(Line("""{"kind":"Item"}""", "PercentOff", "10", "null", "Sale")));

        Assert.StartsWith("linePromotions[0].target.id:", errors.Single());
    }

    [TestMethod]
    public void ExampleParametersAreValid()
    {
        Assert.IsEmpty(new PriceAdjustmentRuleType().Validate(J("""{"entries":[{"target":{"kind":"Type","id":1},"mode":"PercentOff","value":10}]}""")));
        Assert.IsEmpty(new PromotionRuleType().Validate(J("""{"linePromotions":[{"target":{"kind":"Item","id":99},"kind":"PercentOff","value":10,"minUnits":null,"label":"10% off"}],"orderPromotions":[]}""")));
    }

    [TestMethod]
    public void ParameterSchemasAreValidJson()
    {
        using var adjustment = JsonDocument.Parse(new PriceAdjustmentRuleType().ParameterSchemaJson);
        using var promotion = JsonDocument.Parse(new PromotionRuleType().ParameterSchemaJson);

        Assert.AreEqual(JsonValueKind.Object, adjustment.RootElement.ValueKind);
        Assert.AreEqual(JsonValueKind.Object, promotion.RootElement.ValueKind);
    }

    [TestMethod]
    public void ResolveReturnsPublishedPromotionAndFallsBackWhenInvalid()
    {
        var type = new PriceAdjustmentRuleType();
        var published = type.Resolve(Snapshot(Rule("PRC-001", """{"entries":[{"target":{"kind":"All"},"mode":"PercentOff","value":5}]}""")), Now);
        var invalid = type.Resolve(Snapshot(Rule("PRC-001", """{"entries":[{"target":{"kind":"All"},"mode":"PercentOff","value":95}]}""")), Now);

        Assert.HasCount(1, published.Entries);
        Assert.IsEmpty(invalid.Entries);
    }

    [TestMethod]
    public void ResolveFallsBackToDefaultWhenParametersAreUndefined()
    {
        var snapshot = new RulesSnapshot(1, Now, [new SnapshotRuleVersion("PRC-001", RuleScopes.Default, 1, Now.AddHours(-1), null, default)]);
        var type = new PriceAdjustmentRuleType();

        Assert.IsEmpty(type.Resolve(snapshot, Now).Entries);
        Assert.IsNotEmpty(type.Validate(default));
    }

    [TestMethod]
    public void AddBusinessRuleTypesRegistersAllFive()
    {
        var registry = new ServiceCollection().AddBusinessRuleTypes().BuildServiceProvider().GetRequiredService<RuleTypeRegistry>();

        foreach (var id in new[] { "PRC-001", "PRM-001", "ORD-001", "ORD-013", "SHP-001" })
        {
            Assert.IsNotNull(registry.Find(id), id);
        }

        Assert.HasCount(5, registry.All);
    }

    private static string Entries(params (string Target, string Mode, string Value)[] entries)
        => $$"""{"entries":[{{string.Join(',', entries.Select(e => $$"""{"target":{{e.Target}},"mode":"{{e.Mode}}","value":{{e.Value}}}"""))}}]}""";

    private static string Line(string target, string kind, string value, string minUnits, string label)
        => $$"""{"linePromotions":[{"target":{{target}},"kind":"{{kind}}","value":{{value}},"minUnits":{{minUnits}},"label":"{{label}}"}],"orderPromotions":[]}""";

    private static string Order(string kind, string value, string minSubtotal, string label)
        => $$"""{"linePromotions":[],"orderPromotions":[{"kind":"{{kind}}","value":{{value}},"minSubtotal":{{minSubtotal}},"label":"{{label}}"}]}""";
}
