using eShop.BusinessRules.Client.Rules.Types;
using Microsoft.Extensions.DependencyInjection;
using static eShop.BusinessRules.UnitTests.SnapshotFactory;

namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class GlobalRuleTypesTests
{
    [TestMethod]
    public void GracePeriodDefaultIsOneMinute()
        => Assert.AreEqual(1, new GracePeriodRuleType().Resolve(RulesSnapshot.Empty, Now).Minutes);

    [TestMethod]
    [DataRow("""{"minutes":0}""")]
    [DataRow("""{"minutes":61}""")]
    [DataRow("""{}""")]
    public void GracePeriodOutsideOneToSixtyIsInvalid(string json)
        => Assert.IsNotEmpty(new GracePeriodRuleType().Validate(J(json)));

    [TestMethod]
    public void GracePeriodOfSixtyIsValid()
        => Assert.IsEmpty(new GracePeriodRuleType().Validate(J("""{"minutes":60}""")));

    [TestMethod]
    public void UnknownMemberIsInvalid()
        => Assert.IsNotEmpty(new GracePeriodRuleType().Validate(J("""{"minutes":5,"minuets":5}""")));

    [TestMethod]
    public void ShortfallPolicyRejectsIntegerEnum()
        => Assert.IsNotEmpty(new StockShortfallPolicyRuleType().Validate(J("""{"policy":1}""")));

    [TestMethod]
    [DataRow("""{"policy":"1"}""")]
    [DataRow("""{"policy":"Other"}""")]
    public void ShortfallPolicyRejectsUnknownName(string json)
        => Assert.IsNotEmpty(new StockShortfallPolicyRuleType().Validate(J(json)));

    [TestMethod]
    public void ShortfallPolicyResolvesPublishedValue()
        => Assert.AreEqual(
            ShortfallPolicy.PartialFulfilment,
            new StockShortfallPolicyRuleType().Resolve(Snapshot(Rule("ORD-013", """{"policy":"PartialFulfilment"}""")), Now).Policy);

    [TestMethod]
    [DataRow("""{"fee":100.01}""")]
    [DataRow("""{"fee":-1}""")]
    [DataRow("""{"fee":4.999}""")]
    [DataRow("""{"fee":5,"freeShippingThreshold":-0.01}""")]
    public void ShippingFeeOutOfRangeIsInvalid(string json)
        => Assert.IsNotEmpty(new ShippingFeeRuleType().Validate(J(json)));

    [TestMethod]
    public void ResolveFallsBackToDefaultWhenParametersAreInvalid()
        => Assert.AreEqual(1, new GracePeriodRuleType().Resolve(Snapshot(Rule("ORD-001", """{"minutes":"ten"}""")), Now).Minutes);

    [TestMethod]
    public void DefaultSerializesCamelCase()
        => Assert.AreEqual(0m, new ShippingFeeRuleType().Default.GetProperty("fee").GetDecimal());

    [TestMethod]
    public void AddBusinessRuleTypesRegistersGlobalTypes()
    {
        var registry = new ServiceCollection().AddBusinessRuleTypes().BuildServiceProvider().GetRequiredService<RuleTypeRegistry>();

        Assert.IsNotNull(registry.Find("ORD-001"));
        Assert.IsNotNull(registry.Find("ORD-013"));
        Assert.IsNotNull(registry.Find("SHP-001"));
    }

    [TestMethod]
    public void AddBusinessRuleTypesIsIdempotent()
    {
        var registry = new ServiceCollection().AddBusinessRuleTypes().AddBusinessRuleTypes().BuildServiceProvider().GetRequiredService<RuleTypeRegistry>();

        Assert.HasCount(5, registry.All);
    }
}
