using System.Threading;
using eShop.BusinessRules.Client.Snapshot;

namespace eShop.Ordering.FunctionalTests;

/// <summary>Simulates BusinessRules.API being down: every fetch fails.</summary>
public sealed class UnavailableRulesSnapshotSource : IRulesSnapshotSource
{
    private readonly TaskCompletionSource _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Completes once the snapshot cache has tried (and failed) to fetch at least once.</summary>
    public Task FirstCallAttempted => _firstCall.Task;

    public Task<SnapshotFetchResult> FetchAsync(long? knownVersion, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        _firstCall.TrySetResult();
        throw new HttpRequestException("BusinessRules.API is unavailable.");
    }
}
