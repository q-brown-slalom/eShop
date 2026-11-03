using System.Text.Json;

namespace eShop.BusinessRules.API.Model;

/// <summary>
/// One version of a rule type's parameters for a scope.
/// Retention: kept 7 years after it is superseded (data-retention.md).
/// </summary>
public class RuleVersion
{
    private RuleVersion()
    {
        RuleTypeId = null!;
        Scope = null!;
        Parameters = null!;
        CreatedBy = null!;
    }

    private RuleVersion(
        string ruleTypeId,
        string scope,
        int versionNumber,
        JsonDocument parameters,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        string createdBy,
        DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7(createdAt);
        RuleTypeId = ruleTypeId;
        Scope = scope;
        VersionNumber = versionNumber;
        Parameters = parameters;
        Status = RuleVersionStatus.Draft;
        CreatedBy = createdBy;
        CreatedAt = createdAt.ToUniversalTime();
        SetEffectiveWindow(effectiveFrom, effectiveTo);
    }

    public Guid Id { get; private set; }

    public string RuleTypeId { get; private set; }

    public string Scope { get; private set; }

    public int VersionNumber { get; private set; }

    public JsonDocument Parameters { get; private set; }

    public DateTimeOffset EffectiveFrom { get; private set; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public RuleVersionStatus Status { get; private set; }

    public string CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? PublishedBy { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public static RuleVersion CreateDraft(
        string ruleTypeId,
        string scope,
        int versionNumber,
        JsonDocument parameters,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        string createdBy,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(ruleTypeId);
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentOutOfRangeException.ThrowIfLessThan(versionNumber, 1);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrEmpty(createdBy);

        return new RuleVersion(ruleTypeId, scope, versionNumber, parameters, effectiveFrom, effectiveTo, createdBy, createdAt);
    }

    public void UpdateDraft(JsonDocument parameters, DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        EnsureDraft("edited");

        SetEffectiveWindow(effectiveFrom, effectiveTo);
        Parameters = parameters;
    }

    /// <summary>
    /// Publishes the draft. An <see cref="EffectiveFrom"/> in the past is clamped to <paramref name="now"/>
    /// so the published window never claims to have applied before it was published (D5).
    /// </summary>
    public void Publish(string actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(actor);
        EnsureDraft("published");

        now = now.ToUniversalTime();
        var effectiveFrom = EffectiveFrom < now ? now : EffectiveFrom;
        if (EffectiveTo is { } effectiveTo && effectiveTo <= effectiveFrom)
        {
            throw new InvalidOperationException($"Rule version {Id} cannot be published because its effective window has already ended.");
        }

        EffectiveFrom = effectiveFrom;
        Status = RuleVersionStatus.Published;
        PublishedBy = actor;
        PublishedAt = now;
    }

    public void Supersede()
    {
        if (Status != RuleVersionStatus.Published)
        {
            throw new InvalidOperationException($"Rule version {Id} is {Status} and cannot be superseded.");
        }

        Status = RuleVersionStatus.Superseded;
    }

    private void EnsureDraft(string operation)
    {
        if (Status != RuleVersionStatus.Draft)
        {
            throw new InvalidOperationException($"Rule version {Id} is {Status} and cannot be {operation}.");
        }
    }

    private void SetEffectiveWindow(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo)
    {
        if (effectiveTo is { } to && to <= effectiveFrom)
        {
            throw new ArgumentException("EffectiveTo must be later than EffectiveFrom.", nameof(effectiveTo));
        }

        EffectiveFrom = effectiveFrom.ToUniversalTime();
        EffectiveTo = effectiveTo?.ToUniversalTime();
    }
}
