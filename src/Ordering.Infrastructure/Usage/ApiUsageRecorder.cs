namespace eShop.Ordering.Infrastructure.Usage;

public class ApiUsageRecorder(OrderingContext context, TimeProvider timeProvider) : IApiUsageRecorder
{
    public const int RetentionDays = 90;

    public async Task RecordAsync(string operation, CancellationToken ct)
    {
        var today = Today();

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ordering.apiusagedaily ("Day", "Operation", "Calls")
            VALUES ({today}, {operation}, 1)
            ON CONFLICT ("Day", "Operation") DO UPDATE SET "Calls" = apiusagedaily."Calls" + 1
            """,
            ct);

        var retentionCutoff = today.AddDays(-RetentionDays);
        await context.ApiUsageDaily
            .Where(u => u.Day < retentionCutoff)
            .ExecuteDeleteAsync(ct);
    }

    public Task<long> GetCallsAsync(string operation, int days, CancellationToken ct)
    {
        // Window is (today - days, today]: days=7 counts today and the 6 days before it.
        var windowStart = Today().AddDays(-days);

        return context.ApiUsageDaily
            .Where(u => u.Operation == operation && u.Day > windowStart)
            .SumAsync(u => u.Calls, ct);
    }

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
}
