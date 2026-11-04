namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class RulesSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonElement EmptyParameters = JsonDocument.Parse("{}").RootElement.Clone();

    private static SnapshotRuleVersion V(int versionNumber, int fromDays, int? to = null, string scope = RuleScopes.Default)
        => new(TestRuleType.TypeId, scope, versionNumber, Now.AddDays(fromDays), to is { } toDays ? Now.AddDays(toDays) : null, EmptyParameters);

    private static RulesSnapshot Snapshot(params SnapshotRuleVersion[] rules) => new(1, Now, rules);

    [TestMethod]
    public void FindEffectiveReturnsNullWhenNoVersions()
        => Assert.IsNull(RulesSnapshot.Empty.FindEffective(TestRuleType.TypeId, RuleScopes.Default, Now));

    [TestMethod]
    public void FindEffectivePicksLatestEffectiveFromNotInFuture()
        => Assert.AreEqual(2, Snapshot(V(1, -2), V(2, -1), V(3, +1)).FindEffective(TestRuleType.TypeId, RuleScopes.Default, Now)!.VersionNumber);

    [TestMethod]
    public void FindEffectiveSkipsExpiredVersionAndFallsBackToEarlier()
        => Assert.AreEqual(1, Snapshot(V(1, -3), V(2, -2, to: -1)).FindEffective(TestRuleType.TypeId, RuleScopes.Default, Now)!.VersionNumber);

    [TestMethod]
    public void FindEffectiveBreaksEffectiveFromTieByHighestVersion()
        => Assert.AreEqual(2, Snapshot(V(2, -1), V(1, -1)).FindEffective(TestRuleType.TypeId, RuleScopes.Default, Now)!.VersionNumber);

    [TestMethod]
    public void FindEffectiveTreatsEffectiveToAsExclusive()
        => Assert.IsNull(Snapshot(V(1, -1, to: 0)).FindEffective(TestRuleType.TypeId, RuleScopes.Default, Now));

    [TestMethod]
    public void FindEffectiveIncludesVersionStartingExactlyNow()
        => Assert.AreEqual(1, Snapshot(V(1, 0)).FindEffective(TestRuleType.TypeId, RuleScopes.Default, Now)!.VersionNumber);

    [TestMethod]
    public void FindEffectiveIgnoresOtherRuleTypesAndScopes()
    {
        var snapshot = Snapshot(
            V(1, -1, scope: "other"),
            new SnapshotRuleVersion("OTH-001", RuleScopes.Default, 2, Now.AddDays(-1), null, EmptyParameters));

        Assert.IsNull(snapshot.FindEffective(TestRuleType.TypeId, RuleScopes.Default, Now));
        Assert.AreEqual(1, snapshot.FindEffective(TestRuleType.TypeId, "other", Now)!.VersionNumber);
    }

    [TestMethod]
    public void EmptySnapshotHasVersionZeroAndNoRules()
    {
        Assert.AreEqual(0L, RulesSnapshot.Empty.Version);
        Assert.IsEmpty(RulesSnapshot.Empty.Rules);
    }
}
