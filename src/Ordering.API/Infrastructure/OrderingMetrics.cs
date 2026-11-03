using System.Diagnostics.Metrics;

namespace eShop.Ordering.API.Infrastructure;

public sealed class OrderingMetrics
{
    public const string MeterName = "eShop.Ordering";

    private readonly Counter<long> _createOrderV1Calls;
    private readonly Counter<long> _totalMismatches;
    private readonly Histogram<double> _quoteDuration;

    public OrderingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _createOrderV1Calls = meter.CreateCounter<long>(
            IApiUsageRecorder.CreateOrderV1Operation,
            unit: "{call}",
            description: "Number of calls to the v1 create-order endpoint.");

        _totalMismatches = meter.CreateCounter<long>(
            "pricing.total_mismatch.count",
            unit: "{order}",
            description: "Number of create-order requests rejected because the expected total did not match the priced total.");

        _quoteDuration = meter.CreateHistogram<double>(
            "pricing.quote.duration",
            unit: "ms",
            description: "Time taken to price a quote.");
    }

    public void CreateOrderV1Called() => _createOrderV1Calls.Add(1);

    public void TotalMismatch() => _totalMismatches.Add(1);

    public void QuoteDuration(TimeSpan elapsed) => _quoteDuration.Record(elapsed.TotalMilliseconds);
}
