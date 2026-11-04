using System.Diagnostics.Metrics;

namespace eShop.BusinessRules.Client.Snapshot;

public sealed class BusinessRulesClientMetrics
{
    public const string MeterName = "eShop.BusinessRules.Client";

    public BusinessRulesClientMetrics(IMeterFactory meterFactory, RulesSnapshotCache cache, TimeProvider timeProvider)
    {
        var meter = meterFactory.Create(MeterName);

        meter.CreateObservableGauge(
            "rules.snapshot.version",
            () => cache.Current.Version,
            description: "Version of the rules snapshot currently cached by this service (0 = built-in defaults).");

        meter.CreateObservableGauge(
            "rules.snapshot.age_seconds",
            () => ObserveAge(cache, timeProvider),
            unit: "s",
            description: "Seconds since the cached rules snapshot was published.");
    }

    private static IEnumerable<Measurement<double>> ObserveAge(RulesSnapshotCache cache, TimeProvider timeProvider)
    {
        var snapshot = cache.Current;
        if (snapshot.Version == 0)
        {
            return [];
        }

        return [new Measurement<double>((timeProvider.GetUtcNow() - snapshot.PublishedAt).TotalSeconds)];
    }
}
