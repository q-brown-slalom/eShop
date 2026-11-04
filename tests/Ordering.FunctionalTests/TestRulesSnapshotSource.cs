using System.Threading;
using eShop.BusinessRules.Client.Snapshot;

namespace eShop.Ordering.FunctionalTests;

/// <summary>
/// Stands in for BusinessRules.API. Every fetch fails (BusinessRules.API down) until a test sets <see cref="Snapshot"/>,
/// after which fetches return it.
/// </summary>
public sealed class TestRulesSnapshotSource : IRulesSnapshotSource
{
    private readonly TaskCompletionSource _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;
    private RulesSnapshot _snapshot;

    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Completes once the snapshot cache has tried to fetch at least once.</summary>
    public Task FirstCallAttempted => _firstCall.Task;

    /// <summary>The snapshot returned by fetches; <see langword="null"/> makes every fetch fail.</summary>
    public RulesSnapshot Snapshot
    {
        get => Volatile.Read(ref _snapshot);
        set => Volatile.Write(ref _snapshot, value);
    }

    public Task<SnapshotFetchResult> FetchAsync(long? knownVersion, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        _firstCall.TrySetResult();

        var snapshot = Snapshot;
        if (snapshot is null)
        {
            throw new HttpRequestException("BusinessRules.API is unavailable.");
        }

        return Task.FromResult(new SnapshotFetchResult(false, snapshot));
    }
}
