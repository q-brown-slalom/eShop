using eShop.BusinessRules.Client.IntegrationEvents;
using Npgsql;

namespace eShop.BusinessRules.API.Services;

public enum PublishOutcomeKind
{
    Published,
    NotFound,
    Conflict,
    GuardRejected,
}

/// <param name="ProblemType">For <see cref="PublishOutcomeKind.Conflict"/> or <see cref="PublishOutcomeKind.GuardRejected"/>, the ProblemDetails type.</param>
public sealed record PublishOutcome(PublishOutcomeKind Kind, long SnapshotVersion = 0, string? ProblemType = null, string? Title = null, string? Detail = null);

/// <summary>
/// Publishes a draft rule version: in one transaction it publishes the draft, supersedes the versions it replaces,
/// stores the next snapshot version, audits the publish and saves the integration event to the outbox.
/// The event is sent to the bus after the transaction commits.
/// </summary>
public sealed class RulePublishingService(
    RulesContext context,
    RuleTypeRegistry registry,
    IPublishGuard guard,
    IRulesIntegrationEventService integrationEventService,
    TimeProvider timeProvider,
    BusinessRulesMetrics metrics,
    ILogger<RulePublishingService> logger)
{
    public async Task<PublishOutcome> PublishAsync(Guid ruleVersionId, string actor, string? guardOverrideReason, CancellationToken cancellationToken)
    {
        var version = await context.RuleVersions.SingleOrDefaultAsync(v => v.Id == ruleVersionId, cancellationToken);
        if (version is null)
        {
            return new(PublishOutcomeKind.NotFound);
        }

        if (version.Status != RuleVersionStatus.Draft)
        {
            return Conflict("Rule version is not a draft", $"Rule version {ruleVersionId} is {version.Status}; only drafts can be published.");
        }

        var type = registry.Find(version.RuleTypeId);
        if (type is null)
        {
            return Conflict("Rule type is not registered", $"Rule type '{version.RuleTypeId}' is no longer registered, so its versions cannot be published.");
        }

        var guardResult = await guard.CheckAsync(type, guardOverrideReason, cancellationToken);
        if (!guardResult.Allowed)
        {
            return new(PublishOutcomeKind.GuardRejected, ProblemType: guardResult.ProblemType, Detail: guardResult.Detail);
        }

        var now = timeProvider.GetUtcNow();
        var before = RulesAdminApi.ToAuditJson(version);
        try
        {
            version.Publish(actor, now);
        }
        catch (InvalidOperationException)
        {
            return Conflict("Rule version window has ended", $"Rule version {ruleVersionId} cannot be published because its effective window has already ended.");
        }

        // Read the latest snapshot version before the published versions: a publish that commits in between then
        // makes this one collide on the snapshot version (409) instead of producing a snapshot that misses it.
        var snapshotVersion = (await context.RulesSnapshots.MaxAsync(s => (long?)s.Version, cancellationToken) ?? 0) + 1;
        var published = await context.RuleVersions
            .Where(v => v.Status == RuleVersionStatus.Published)
            .ToListAsync(cancellationToken);
        published.Add(version);

        var (snapshot, supersededIds) = RulesSnapshotBuilder.Build(snapshotVersion, now, published, now);
        foreach (var superseded in published.Where(v => supersededIds.Contains(v.Id)))
        {
            superseded.Supersede();
        }

        context.RulesSnapshots.Add(RulesSnapshotRecord.Create(snapshot.Version, snapshot.PublishedAt, snapshot.Rules));
        if (guardResult.Overridden)
        {
            context.AuditEntries.Add(new AuditEntry(
                now, actor, AuditAction.GuardOverridden, version.RuleTypeId, version.Scope, version.Id,
                beforeJson: null, afterJson: null, reason: guardOverrideReason?.Trim()));
        }

        context.AuditEntries.Add(new AuditEntry(
            now, actor, AuditAction.Published, version.RuleTypeId, version.Scope, version.Id,
            beforeJson: before, afterJson: RulesAdminApi.ToAuditJson(version)));

        var integrationEvent = new BusinessRulesSnapshotPublishedIntegrationEvent(snapshot.Version, snapshot.PublishedAt);
        try
        {
            await integrationEventService.SaveEventAndRulesContextChangesAsync(integrationEvent);
        }
        catch (Exception ex) when (IsConcurrentPublish(ex))
        {
            logger.LogWarning("Rules snapshot {SnapshotVersion} was not published because another publish happened at the same time", snapshot.Version);
            return Conflict(
                "Rules were published concurrently",
                "Another publish changed the rules at the same time; reload and try again.",
                PublishProblemTypes.ConcurrentPublish);
        }

        metrics.RulePublished();
        logger.LogInformation("Rules snapshot {SnapshotVersion} published", snapshot.Version);

        await integrationEventService.PublishThroughEventBusAsync(integrationEvent);

        return new(PublishOutcomeKind.Published, snapshot.Version);
    }

    // A changed rule version (xmin) or a snapshot version taken by another publish means a concurrent publish won.
    private static bool IsConcurrentPublish(Exception ex)
        => ex is DbUpdateConcurrencyException
            || ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, TableName: "snapshots" } };

    private static PublishOutcome Conflict(string title, string detail, string? problemType = null)
        => new(PublishOutcomeKind.Conflict, ProblemType: problemType, Title: title, Detail: detail);
}
