using eShop.EventBus.Events;

namespace eShop.BusinessRules.API.Services;

public interface IRulesIntegrationEventService
{
    Task SaveEventAndRulesContextChangesAsync(IntegrationEvent evt);
    Task PublishThroughEventBusAsync(IntegrationEvent evt);
}
