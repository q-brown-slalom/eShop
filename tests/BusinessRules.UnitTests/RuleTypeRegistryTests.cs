using Microsoft.Extensions.DependencyInjection;

namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class RuleTypeRegistryTests
{
    [TestMethod]
    public void RegistryFindReturnsRegisteredTypeAndNullOtherwise()
    {
        var registry = new RuleTypeRegistry([new TestRuleType()]);

        Assert.IsInstanceOfType<TestRuleType>(registry.Find(TestRuleType.TypeId));
        Assert.IsNull(registry.Find("NOPE-001"));
        Assert.HasCount(1, registry.All);
    }

    [TestMethod]
    public void RegistryRejectsDuplicateIds()
        => Assert.ThrowsExactly<ArgumentException>(() => new RuleTypeRegistry([new TestRuleType(), new TestRuleType()]));

    [TestMethod]
    public void AddRuleTypeRegistersTypeAndRegistry()
    {
        var services = new ServiceCollection();
        services.AddRuleType<TestRuleType>();
        services.AddRuleType<TestRuleType>();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<RuleTypeRegistry>();

        Assert.IsNotNull(registry.Find(TestRuleType.TypeId));
        Assert.HasCount(1, registry.All);
    }
}
