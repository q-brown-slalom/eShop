using eShop.BusinessRules.Client.Snapshot;

namespace eShop.BusinessRules.API.Services;

/// <summary>Builds a rules snapshot from the published rule versions, applying the D5 resolution and supersede rules.</summary>
public static class RulesSnapshotBuilder
{
    /// <summary>
    /// A published version is superseded once a version that outranks it (later <c>EffectiveFrom</c>, or the same
    /// <c>EffectiveFrom</c> and a higher <c>VersionNumber</c>) is open-ended and already effective, because it can
    /// never be resolved again. Superseded versions, and versions whose window has ended, are left out of the snapshot;
    /// future-dated versions are kept so consumers switch to them on time.
    /// </summary>
    public static (RulesSnapshot Snapshot, IReadOnlyList<Guid> SupersededIds) Build(
        long version,
        DateTimeOffset publishedAt,
        IEnumerable<RuleVersion> published,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(published);

        var candidates = published.Where(v => v.Status == RuleVersionStatus.Published).ToList();

        var supersededIds = candidates
            .Where(v => candidates.Any(w => Supersedes(w, v, now)))
            .Select(v => v.Id)
            .ToList();

        var rules = candidates
            .Where(v => !supersededIds.Contains(v.Id) && (v.EffectiveTo is null || v.EffectiveTo > now))
            .OrderBy(v => v.RuleTypeId, StringComparer.Ordinal)
            .ThenBy(v => v.Scope, StringComparer.Ordinal)
            .ThenBy(v => v.VersionNumber)
            .Select(v => new SnapshotRuleVersion(
                v.RuleTypeId,
                v.Scope,
                v.VersionNumber,
                v.EffectiveFrom,
                v.EffectiveTo,
                v.Parameters.RootElement.Clone()))
            .ToList();

        return (new RulesSnapshot(version, publishedAt, rules), supersededIds);
    }

    private static bool Supersedes(RuleVersion newer, RuleVersion older, DateTimeOffset now)
        => !ReferenceEquals(newer, older)
            && string.Equals(newer.RuleTypeId, older.RuleTypeId, StringComparison.Ordinal)
            && string.Equals(newer.Scope, older.Scope, StringComparison.Ordinal)
            && newer.EffectiveTo is null
            && newer.EffectiveFrom <= now
            && (newer.EffectiveFrom > older.EffectiveFrom
                || (newer.EffectiveFrom == older.EffectiveFrom && newer.VersionNumber > older.VersionNumber));
}
