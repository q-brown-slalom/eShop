using eShop.BusinessRules.API.Model;

namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class RuleVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void NewDraftHasDraftStatusAndCreator()
    {
        var version = CreateDraft(Now.AddDays(1));

        Assert.AreEqual(RuleVersionStatus.Draft, version.Status);
        Assert.AreEqual("editor-1", version.CreatedBy);
        Assert.AreEqual(Now, version.CreatedAt);
        Assert.IsNull(version.PublishedBy);
        Assert.IsNull(version.PublishedAt);
    }

    [TestMethod]
    public void DraftCanBeUpdated()
    {
        var version = CreateDraft(Now.AddDays(1));
        using var parameters = JsonDocument.Parse("""{"enabled":true}""");

        version.UpdateDraft(parameters, Now.AddDays(2), Now.AddDays(3));

        Assert.IsTrue(version.Parameters.RootElement.GetProperty("enabled").GetBoolean());
        Assert.AreEqual(Now.AddDays(2), version.EffectiveFrom);
        Assert.AreEqual(Now.AddDays(3), version.EffectiveTo);
    }

    [TestMethod]
    public void PublishedVersionCannotBeEdited()
    {
        var version = CreateDraft(Now.AddDays(1));
        version.Publish("publisher-1", Now);
        using var parameters = JsonDocument.Parse("""{"enabled":true}""");

        Assert.ThrowsExactly<InvalidOperationException>(() => version.UpdateDraft(parameters, Now.AddDays(2), null));
    }

    [TestMethod]
    public void PublishedVersionCannotBePublishedAgain()
    {
        var version = CreateDraft(Now.AddDays(1));
        version.Publish("publisher-1", Now);

        Assert.ThrowsExactly<InvalidOperationException>(() => version.Publish("publisher-2", Now));
    }

    [TestMethod]
    public void PublishClampsPastEffectiveFromToNow()
    {
        var version = CreateDraft(Now.AddDays(-5));

        version.Publish("publisher-1", Now);

        Assert.AreEqual(Now, version.EffectiveFrom);
    }

    [TestMethod]
    public void PublishKeepsFutureEffectiveFrom()
    {
        var version = CreateDraft(Now.AddDays(5));

        version.Publish("publisher-1", Now);

        Assert.AreEqual(Now.AddDays(5), version.EffectiveFrom);
    }

    [TestMethod]
    public void PublishSetsPublisherAndTimestamp()
    {
        var version = CreateDraft(Now.AddDays(1));

        version.Publish("publisher-1", Now);

        Assert.AreEqual(RuleVersionStatus.Published, version.Status);
        Assert.AreEqual("publisher-1", version.PublishedBy);
        Assert.AreEqual(Now, version.PublishedAt);
    }

    [TestMethod]
    public void PublishRejectsWindowThatHasAlreadyEnded()
    {
        var version = CreateDraft(Now.AddDays(-5), Now.AddDays(-1));

        Assert.ThrowsExactly<InvalidOperationException>(() => version.Publish("publisher-1", Now));
        Assert.AreEqual(RuleVersionStatus.Draft, version.Status);
    }

    [TestMethod]
    public void UpdateDraftRejectsEffectiveToNotAfterEffectiveFrom()
    {
        var version = CreateDraft(Now.AddDays(1));
        using var parameters = JsonDocument.Parse("""{"enabled":true}""");

        Assert.ThrowsExactly<ArgumentException>(() => version.UpdateDraft(parameters, Now.AddDays(2), Now.AddDays(2)));
    }

    [TestMethod]
    public void SupersedeMovesPublishedVersionToSuperseded()
    {
        var version = CreateDraft(Now.AddDays(1));
        version.Publish("publisher-1", Now);

        version.Supersede();

        Assert.AreEqual(RuleVersionStatus.Superseded, version.Status);
    }

    [TestMethod]
    public void DraftCannotBeSuperseded()
    {
        var version = CreateDraft(Now.AddDays(1));

        Assert.ThrowsExactly<InvalidOperationException>(version.Supersede);
    }

    [TestMethod]
    public void TimestampsAreStoredAsUtc()
    {
        var local = new DateTimeOffset(2026, 11, 3, 5, 0, 0, TimeSpan.FromHours(-7));

        var version = CreateDraft(local);

        Assert.AreEqual(TimeSpan.Zero, version.EffectiveFrom.Offset);
        Assert.AreEqual(local, version.EffectiveFrom);
    }

    private static RuleVersion CreateDraft(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo = null)
        => RuleVersion.CreateDraft(
            TestRuleType.TypeId,
            RuleScopes.Default,
            versionNumber: 1,
            JsonDocument.Parse("""{"enabled":false}"""),
            effectiveFrom,
            effectiveTo,
            createdBy: "editor-1",
            createdAt: Now);
}
