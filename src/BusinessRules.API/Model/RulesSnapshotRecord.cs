using System.Text.Json;
using eShop.BusinessRules.Client.Snapshot;

namespace eShop.BusinessRules.API.Model;

/// <summary>
/// A published, immutable rules snapshot. <see cref="Content"/> holds the snapshot's rules in the
/// <see cref="RulesSnapshotJson"/> wire format.
/// Retention: the latest snapshot, plus every snapshot referenced by an order, is kept 7 years (data-retention.md).
/// </summary>
public class RulesSnapshotRecord
{
    private RulesSnapshotRecord()
    {
        Content = null!;
    }

    private RulesSnapshotRecord(long version, DateTimeOffset publishedAt, JsonDocument content)
    {
        Version = version;
        PublishedAt = publishedAt.ToUniversalTime();
        Content = content;
    }

    public long Version { get; private set; }

    public DateTimeOffset PublishedAt { get; private set; }

    public JsonDocument Content { get; private set; }

    public static RulesSnapshotRecord Create(long version, DateTimeOffset publishedAt, IReadOnlyList<SnapshotRuleVersion> rules)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentNullException.ThrowIfNull(rules);

        return new RulesSnapshotRecord(version, publishedAt, JsonSerializer.SerializeToDocument(rules, RulesSnapshotJson.Options));
    }

    public RulesSnapshot ToSnapshot()
        => new(Version, PublishedAt, Content.Deserialize<List<SnapshotRuleVersion>>(RulesSnapshotJson.Options) ?? []);
}
