using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace eShop.BusinessRules.Client.Snapshot;

/// <summary>Loads the snapshot at startup (bounded by a 5 s timeout) and then polls on <see cref="BusinessRulesClientOptions.PollInterval"/>.</summary>
public sealed class RulesSnapshotRefreshService : BackgroundService
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);

    private readonly RulesSnapshotCache _cache;
    private readonly IOptions<BusinessRulesClientOptions> _options;
    private readonly TimeProvider _timeProvider;

    // Taking the metrics dependency creates its observable gauges for the lifetime of the host.
    public RulesSnapshotRefreshService(
        RulesSnapshotCache cache,
        IOptions<BusinessRulesClientOptions> options,
        TimeProvider timeProvider,
        BusinessRulesClientMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _cache = cache;
        _options = options;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var startup = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
        {
            startup.CancelAfter(StartupTimeout);
            await _cache.RefreshAsync(startup.Token);
        }

        using var timer = new PeriodicTimer(_options.Value.PollInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await _cache.RefreshAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
