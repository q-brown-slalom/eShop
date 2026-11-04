using System.Text.Json;

namespace eShop.BusinessRules.Client.Snapshot;

/// <summary>A published rule version. Effective for <c>EffectiveFrom &lt;= now &lt; EffectiveTo</c>.</summary>
public sealed record SnapshotRuleVersion(
    string RuleTypeId,
    string Scope,
    int VersionNumber,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    JsonElement Parameters)
{
    public bool IsEffectiveAt(DateTimeOffset now)
        => EffectiveFrom <= now && (EffectiveTo is null || now < EffectiveTo);
}
