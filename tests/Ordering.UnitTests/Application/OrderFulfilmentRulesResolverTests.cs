namespace eShop.Ordering.UnitTests.Application;

using System.Text.Json;
using eShop.BusinessRules.Client.Rules;
using eShop.BusinessRules.Client.Rules.Types;
using eShop.BusinessRules.Client.Snapshot;
using eShop.Ordering.API.Application.Rules;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public class OrderFulfilmentRulesResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ResolverReturnsDefaultsForEmptySnapshot()
    {
        var resolver = await CreateResolverAsync();

        var rules = resolver.Resolve();

        Assert.AreEqual(TimeSpan.FromMinutes(1), rules.GracePeriod);
        Assert.AreEqual(StockShortfallPolicy.AllOrNothing, rules.ShortfallPolicy);
    }

    [TestMethod]
    public async Task ResolverMapsPartialFulfilment()
    {
        var resolver = await CreateResolverAsync(
            Rule(GracePeriodRuleType.TypeId, """{"minutes":10}"""),
            Rule(StockShortfallPolicyRuleType.TypeId, """{"policy":"PartialFulfilment"}"""));

        var rules = resolver.Resolve();

        Assert.AreEqual(TimeSpan.FromMinutes(10), rules.GracePeriod);
        Assert.AreEqual(StockShortfallPolicy.PartialFulfilment, rules.ShortfallPolicy);
    }

    [TestMethod]
    public async Task ResolverMapsAllOrNothing()
    {
        var resolver = await CreateResolverAsync(Rule(StockShortfallPolicyRuleType.TypeId, """{"policy":"AllOrNothing"}"""));

        Assert.AreEqual(StockShortfallPolicy.AllOrNothing, resolver.Resolve().ShortfallPolicy);
    }

    [TestMethod]
    public async Task ResolverFallsBackToDefaultsWhenParametersAreInvalid()
    {
        var resolver = await CreateResolverAsync(
            Rule(GracePeriodRuleType.TypeId, """{"minutes":600}"""),
            Rule(StockShortfallPolicyRuleType.TypeId, """{"policy":"Sometimes"}"""));

        var rules = resolver.Resolve();

        Assert.AreEqual(TimeSpan.FromMinutes(1), rules.GracePeriod);
        Assert.AreEqual(StockShortfallPolicy.AllOrNothing, rules.ShortfallPolicy);
    }

    [TestMethod]
    public async Task ResolverIgnoresRulesThatAreNotYetEffective()
    {
        var future = Rule(GracePeriodRuleType.TypeId, """{"minutes":30}""") with { EffectiveFrom = Now.AddHours(1) };
        var resolver = await CreateResolverAsync(future);

        Assert.AreEqual(TimeSpan.FromMinutes(1), resolver.Resolve().GracePeriod);
    }

    private static SnapshotRuleVersion Rule(string ruleTypeId, string parametersJson)
        => new(ruleTypeId, RuleScopes.Default, 1, Now.AddDays(-1), null, JsonDocument.Parse(parametersJson).RootElement.Clone());

    private static async Task<OrderFulfilmentRulesResolver> CreateResolverAsync(params SnapshotRuleVersion[] rules)
    {
        var source = Substitute.For<IRulesSnapshotSource>();
        var timeProvider = new FixedTimeProvider(Now);
        var cache = new RulesSnapshotCache(source, timeProvider, NullLogger<RulesSnapshotCache>.Instance);
        if (rules.Length > 0)
        {
            source.FetchAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>())
                .Returns(new SnapshotFetchResult(false, new RulesSnapshot(1, Now.AddMinutes(-1), rules)));
            Assert.IsTrue(await cache.RefreshAsync(CancellationToken.None));
        }

        return new OrderFulfilmentRulesResolver(cache, timeProvider);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
