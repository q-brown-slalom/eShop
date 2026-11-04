namespace eShop.BusinessRules.Client.Snapshot;

public interface IRulesSnapshotSource
{
    /// <summary>
    /// Fetches the current snapshot. Pass the cached version as <paramref name="knownVersion"/> to get
    /// <see cref="SnapshotFetchResult.NotModified"/> when it is still current.
    /// </summary>
    Task<SnapshotFetchResult> FetchAsync(long? knownVersion, CancellationToken ct);
}

public sealed record SnapshotFetchResult(bool NotModified, RulesSnapshot? Snapshot);
