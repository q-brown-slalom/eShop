using Microsoft.Extensions.Logging;

namespace eShop.BusinessRules.Client.Snapshot;

/// <summary>
/// Holds the last known rules snapshot. Starts at <see cref="RulesSnapshot.Empty"/> (built-in defaults),
/// never throws from <see cref="RefreshAsync"/>, and never moves the version backwards.
/// </summary>
public sealed class RulesSnapshotCache(IRulesSnapshotSource source, TimeProvider timeProvider, ILogger<RulesSnapshotCache> logger)
{
    private readonly Lock _gate = new();
    private RulesSnapshot _current = RulesSnapshot.Empty;
    private DateTimeOffset? _lastRefreshedAt;

    public RulesSnapshot Current => Volatile.Read(ref _current);

    public DateTimeOffset? LastRefreshedAt
    {
        get
        {
            lock (_gate)
            {
                return _lastRefreshedAt;
            }
        }
    }

    /// <returns><see langword="true"/> when the source answered (new, unchanged, or older snapshot); <see langword="false"/> on failure.</returns>
    public async Task<bool> RefreshAsync(CancellationToken ct)
    {
        var known = Current.Version;
        SnapshotFetchResult result;
        try
        {
            result = await source.FetchAsync(known > 0 ? known : null, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogWarning("Rules snapshot refresh was cancelled; keeping snapshot version {RulesSnapshotVersion}", known);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rules snapshot refresh failed; keeping snapshot version {RulesSnapshotVersion}", known);
            return false;
        }

        var now = timeProvider.GetUtcNow();
        lock (_gate)
        {
            _lastRefreshedAt = now;

            if (result.NotModified || result.Snapshot is null)
            {
                return true;
            }

            if (result.Snapshot.Version > _current.Version)
            {
                Volatile.Write(ref _current, result.Snapshot);
                logger.LogInformation("Rules snapshot updated to version {RulesSnapshotVersion}", result.Snapshot.Version);
            }
            else if (result.Snapshot.Version < _current.Version)
            {
                logger.LogWarning(
                    "Ignoring rules snapshot version {ReceivedVersion} older than cached version {RulesSnapshotVersion}",
                    result.Snapshot.Version,
                    _current.Version);
            }
        }

        return true;
    }
}
