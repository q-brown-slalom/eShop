namespace eShop.BusinessRules.API.Model;

/// <summary>
/// An append-only record of a change to rule versions.
/// Retention: kept 7 years after the related version is superseded (data-retention.md).
/// </summary>
public class AuditEntry
{
    private AuditEntry()
    {
        ActorId = null!;
        RuleTypeId = null!;
        Scope = null!;
    }

    public AuditEntry(
        DateTimeOffset occurredAt,
        string actorId,
        AuditAction action,
        string ruleTypeId,
        string scope,
        Guid? ruleVersionId,
        string? beforeJson,
        string? afterJson,
        string? reason = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(actorId);
        ArgumentException.ThrowIfNullOrEmpty(ruleTypeId);
        ArgumentException.ThrowIfNullOrEmpty(scope);

        OccurredAt = occurredAt.ToUniversalTime();
        ActorId = actorId;
        Action = action;
        RuleTypeId = ruleTypeId;
        Scope = scope;
        RuleVersionId = ruleVersionId;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        Reason = reason;
    }

    public long Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string ActorId { get; private set; }

    public AuditAction Action { get; private set; }

    public string RuleTypeId { get; private set; }

    public string Scope { get; private set; }

    public Guid? RuleVersionId { get; private set; }

    public string? BeforeJson { get; private set; }

    public string? AfterJson { get; private set; }

    public string? Reason { get; private set; }
}
