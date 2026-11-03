using eShop.EventBus.Events;

namespace eShop.BusinessRules.Client.IntegrationEvents;

public record BusinessRulesSnapshotPublishedIntegrationEvent(long SnapshotVersion, DateTimeOffset PublishedAt) : IntegrationEvent;
