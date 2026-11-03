namespace eShop.BusinessRules.Client.Snapshot;

public sealed record RulesSnapshot(long Version, DateTimeOffset PublishedAt, IReadOnlyList<SnapshotRuleVersion> Rules)
{
    public static RulesSnapshot Empty { get; } = new(0, DateTimeOffset.MinValue, []);

    /// <summary>
    /// Resolves the effective version (plan decision D5): among versions effective at <paramref name="now"/>,
    /// the latest <c>EffectiveFrom</c> wins, ties broken by highest <c>VersionNumber</c>.
    /// Returns <see langword="null"/> when none is effective, meaning the built-in default applies.
    /// </summary>
    public SnapshotRuleVersion? FindEffective(string ruleTypeId, string scope, DateTimeOffset now)
    {
        SnapshotRuleVersion? best = null;
        foreach (var rule in Rules)
        {
            if (!string.Equals(rule.RuleTypeId, ruleTypeId, StringComparison.Ordinal)
                || !string.Equals(rule.Scope, scope, StringComparison.Ordinal)
                || !rule.IsEffectiveAt(now))
            {
                continue;
            }

            if (best is null
                || rule.EffectiveFrom > best.EffectiveFrom
                || (rule.EffectiveFrom == best.EffectiveFrom && rule.VersionNumber > best.VersionNumber))
            {
                best = rule;
            }
        }

        return best;
    }
}
