using Microsoft.AspNetCore.Http.HttpResults;

public static class UsageApi
{
    public const int MinDays = 1;
    public const int MaxDays = 30;

    public static RouteGroupBuilder MapUsageApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("api/usage").HasApiVersion(1.0);

        api.MapGet("/create-order-v1", GetCreateOrderV1UsageAsync)
            .WithName("GetCreateOrderV1Usage")
            .WithSummary("Get v1 create-order usage")
            .WithDescription($"Returns how many times the v1 create-order endpoint was called over the last `days` UTC days, today included ({MinDays}-{MaxDays}). Requires the `{OrderingPolicies.UsageScope}` scope.")
            .WithTags("Usage");

        return api;
    }

    public static async Task<Results<Ok<ApiUsageResponse>, ValidationProblem>> GetCreateOrderV1UsageAsync(
        IApiUsageRecorder recorder,
        CancellationToken cancellationToken,
        int days = 7)
    {
        if (days is < MinDays or > MaxDays)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(days)] = [$"days must be between {MinDays} and {MaxDays}."]
            });
        }

        const string operation = IApiUsageRecorder.CreateOrderV1Operation;
        var calls = await recorder.GetCallsAsync(operation, days, cancellationToken);

        return TypedResults.Ok(new ApiUsageResponse(operation, days, calls));
    }
}

public record ApiUsageResponse(string Operation, int Days, long Calls);
