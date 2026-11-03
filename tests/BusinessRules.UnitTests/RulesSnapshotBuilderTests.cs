using eShop.BusinessRules.API.Model;
using eShop.BusinessRules.API.Services;

namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class RulesSnapshotBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void BuildIncludesFutureDatedVersions()
    {
        var current = Published(1, effectiveFrom: Now.AddDays(-10), publishedAt: Now.AddDays(-10));
        var future = Published(2, effectiveFrom: Now.AddDays(5), publishedAt: Now);

        var (snapshot, superseded) = RulesSnapshotBuilder.Build(7, Now, [current, future], Now);

        Assert.AreEqual(7, snapshot.Version);
        Assert.AreEqual(Now, snapshot.PublishedAt);
        CollectionAssert.AreEqual(new[] { 1, 2 }, snapshot.Rules.Select(r => r.VersionNumber).ToArray());
        Assert.IsEmpty(superseded);
        Assert.AreEqual(1, snapshot.FindEffective(TestRuleType.TypeId, RuleScopes.Default, Now)?.VersionNumber);
        Assert.AreEqual(2, snapshot.FindEffective(TestRuleType.TypeId, RuleScopes.Default, Now.AddDays(5))?.VersionNumber);
    }

    [TestMethod]
    public void BuildSupersedesOlderOpenEndedVersionOnceNewerIsEffective()
    {
        var older = Published(1, effectiveFrom: Now.AddDays(-10), publishedAt: Now.AddDays(-10));
        var newer = Published(2, effectiveFrom: Now.AddDays(-1), publishedAt: Now);

        var (snapshot, superseded) = RulesSnapshotBuilder.Build(2, Now, [older, newer], Now);

        CollectionAssert.AreEqual(new[] { older.Id }, superseded.ToArray());
        Assert.AreEqual(2, snapshot.Rules.Single().VersionNumber);
        Assert.AreEqual(newer.Parameters.RootElement.GetRawText(), snapshot.Rules.Single().Parameters.GetRawText());
    }

    [TestMethod]
    public void BuildKeepsOlderVersionWhenNewerHasEffectiveTo()
    {
        var older = Published(1, effectiveFrom: Now.AddDays(-10), publishedAt: Now.AddDays(-10));
        var newer = Published(2, effectiveFrom: Now, publishedAt: Now, effectiveTo: Now.AddDays(3));

        var (snapshot, superseded) = RulesSnapshotBuilder.Build(2, Now, [older, newer], Now);

        Assert.IsEmpty(superseded);
        CollectionAssert.AreEqual(new[] { 1, 2 }, snapshot.Rules.Select(r => r.VersionNumber).ToArray());
        Assert.AreEqual(2, snapshot.FindEffective(TestRuleType.TypeId, RuleScopes.Default, Now)?.VersionNumber);
        Assert.AreEqual(1, snapshot.FindEffective(TestRuleType.TypeId, RuleScopes.Default, Now.AddDays(3))?.VersionNumber);
    }

    [TestMethod]
    public void BuildDoesNotSupersedeAcrossRuleTypes()
    {
        var first = Published(1, effectiveFrom: Now.AddDays(-10), publishedAt: Now.AddDays(-10));
        var otherType = Published(1, effectiveFrom: Now, publishedAt: Now, ruleTypeId: "TST-002");

        var (snapshot, superseded) = RulesSnapshotBuilder.Build(2, Now, [first, otherType], Now);

        Assert.IsEmpty(superseded);
        Assert.HasCount(2, snapshot.Rules);
    }

    [TestMethod]
    public void BuildOmitsVersionsWhoseWindowHasEnded()
    {
        var expired = Published(1, effectiveFrom: Now.AddDays(-10), publishedAt: Now.AddDays(-10), effectiveTo: Now.AddDays(-1));

        var (snapshot, superseded) = RulesSnapshotBuilder.Build(2, Now, [expired], Now);

        Assert.IsEmpty(superseded);
        Assert.IsEmpty(snapshot.Rules);
    }

    private static RuleVersion Published(
        int versionNumber,
        DateTimeOffset effectiveFrom,
        DateTimeOffset publishedAt,
        DateTimeOffset? effectiveTo = null,
        string ruleTypeId = TestRuleType.TypeId)
    {
        var version = RuleVersion.CreateDraft(
            ruleTypeId,
            RuleScopes.Default,
            versionNumber,
            JsonDocument.Parse($$"""{"enabled":true,"v":{{versionNumber}}}"""),
            effectiveFrom,
            effectiveTo,
            "editor-1",
            publishedAt.AddDays(-1));
        version.Publish("publisher-1", publishedAt);
        return version;
    }
}
