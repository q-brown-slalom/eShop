namespace eShop.BusinessRules.API.Apis;

public record RuleTypeDto(
    string Id,
    string Name,
    bool AffectsPrice,
    RuleTargeting Targeting,
    JsonElement Default,
    JsonElement ParameterSchema)
{
    public static RuleTypeDto From(IRuleType type)
    {
        using var schema = JsonDocument.Parse(type.ParameterSchemaJson);
        return new(type.Id, type.Name, type.AffectsPrice, type.Targeting, type.Default, schema.RootElement.Clone());
    }
}

public record RuleVersionDto(
    Guid Id,
    string RuleTypeId,
    string Scope,
    int VersionNumber,
    JsonElement Parameters,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    RuleVersionStatus Status,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string? PublishedBy,
    DateTimeOffset? PublishedAt)
{
    public static RuleVersionDto From(RuleVersion version)
        => new(
            version.Id,
            version.RuleTypeId,
            version.Scope,
            version.VersionNumber,
            version.Parameters.RootElement.Clone(),
            version.EffectiveFrom,
            version.EffectiveTo,
            version.Status,
            version.CreatedBy,
            version.CreatedAt,
            version.PublishedBy,
            version.PublishedAt);
}

public record AuditEntryDto(
    long Id,
    DateTimeOffset OccurredAt,
    string ActorId,
    AuditAction Action,
    string RuleTypeId,
    string Scope,
    Guid? RuleVersionId,
    string? BeforeJson,
    string? AfterJson,
    string? Reason)
{
    public static AuditEntryDto From(AuditEntry entry)
        => new(
            entry.Id,
            entry.OccurredAt,
            entry.ActorId,
            entry.Action,
            entry.RuleTypeId,
            entry.Scope,
            entry.RuleVersionId,
            entry.BeforeJson,
            entry.AfterJson,
            entry.Reason);
}

public record CreateDraftRequest(
    string RuleTypeId,
    string Scope,
    JsonElement Parameters,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public record UpdateDraftRequest(
    JsonElement Parameters,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public record PublishRequest(string? GuardOverrideReason);

public record PublishResultDto(Guid RuleVersionId, long SnapshotVersion);
