namespace eShop.BusinessRules.UnitTests;

internal static class SnapshotFactory
{
    public static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    public static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static RulesSnapshot Snapshot(params SnapshotRuleVersion[] rules) => new(1, Now, rules);

    public static SnapshotRuleVersion Rule(string typeId, string json, int version = 1, int fromHours = -1)
        => new(typeId, RuleScopes.Default, version, Now.AddHours(fromHours), null, J(json));
}
