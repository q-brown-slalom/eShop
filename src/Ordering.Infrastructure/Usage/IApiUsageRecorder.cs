namespace eShop.Ordering.Infrastructure.Usage;

public interface IApiUsageRecorder
{
    const string CreateOrderV1Operation = "ordering.create_order.v1.calls";

    /// <summary>Adds one call to today's (UTC) count and deletes rows past the 90-day retention.</summary>
    Task RecordAsync(string operation, CancellationToken ct);

    /// <summary>Sums calls over the last <paramref name="days"/> UTC days, today included.</summary>
    Task<long> GetCallsAsync(string operation, int days, CancellationToken ct);
}
