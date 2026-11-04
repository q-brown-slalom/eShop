using eShop.BusinessRules.Client.Snapshot;
using eShop.EventBus.Abstractions;
using Microsoft.Extensions.Logging;

namespace eShop.BusinessRules.Client.IntegrationEvents;

/// <summary>Refreshes the snapshot cache when a newer snapshot is published. Idempotent: stale or replayed events are ignored.</summary>
public sealed class BusinessRulesSnapshotPublishedIntegrationEventHandler(
    RulesSnapshotCache cache,
    ILogger<BusinessRulesSnapshotPublishedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<BusinessRulesSnapshotPublishedIntegrationEvent>
{
    public async Task Handle(BusinessRulesSnapshotPublishedIntegrationEvent @event)
    {
        var cachedVersion = cache.Current.Version;
        if (@event.SnapshotVersion <= cachedVersion)
        {
            logger.LogDebug(
                "Skipping rules snapshot refresh for published version {PublishedVersion}; cached version {RulesSnapshotVersion}",
                @event.SnapshotVersion,
                cachedVersion);
            return;
        }

        await cache.RefreshAsync(CancellationToken.None);
    }
}
