using eShop.EventBus.Abstractions;
using eShop.EventBus.Events;
using eShop.IntegrationEventLogEF.Utilities;

namespace eShop.BusinessRules.API.Services;

public sealed class RulesIntegrationEventService(ILogger<RulesIntegrationEventService> logger,
    IEventBus eventBus,
    RulesContext rulesContext,
    IIntegrationEventLogService integrationEventLogService)
    : IRulesIntegrationEventService, IDisposable
{
    private volatile bool disposedValue;

    public async Task PublishThroughEventBusAsync(IntegrationEvent evt)
    {
        try
        {
            logger.LogInformation("Publishing integration event: {IntegrationEventId_published} - ({@IntegrationEvent})", evt.Id, evt);

            await integrationEventLogService.MarkEventAsInProgressAsync(evt.Id);
            await eventBus.PublishAsync(evt);
            await integrationEventLogService.MarkEventAsPublishedAsync(evt.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error Publishing integration event: {IntegrationEventId} - ({@IntegrationEvent})", evt.Id, evt);
            await integrationEventLogService.MarkEventAsFailedAsync(evt.Id);
        }
    }

    public async Task SaveEventAndRulesContextChangesAsync(IntegrationEvent evt)
    {
        logger.LogInformation("RulesIntegrationEventService - Saving changes and integrationEvent: {IntegrationEventId}", evt.Id);

        //Use of an EF Core resiliency strategy when using multiple DbContexts within an explicit BeginTransaction():
        //See: https://docs.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency
        await ResilientTransaction.New(rulesContext).ExecuteAsync(async () =>
        {
            // Achieving atomicity between the rules database operation and the IntegrationEventLog thanks to a local transaction
            await rulesContext.SaveChangesAsync();
            await integrationEventLogService.SaveEventAsync(evt, rulesContext.Database.CurrentTransaction!);
        });
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                (integrationEventLogService as IDisposable)?.Dispose();
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
