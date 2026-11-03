using System.Globalization;
using eShop.BusinessRules.Client.Snapshot;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace eShop.BusinessRules.API.Apis;

public static class RulesRuntimeApi
{
    public static IEndpointRouteBuilder MapRulesRuntimeApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("RulesRuntime")
            .MapGroup("api/rules")
            .HasApiVersion(1, 0)
            .RequireAuthorization(RulesPolicies.Runtime);

        api.MapGet("/snapshot", GetSnapshot)
            .WithName("GetRulesSnapshot")
            .WithSummary("Get the rules snapshot")
            .WithDescription("Get the latest published rules snapshot. The ETag is the quoted snapshot version; send it in If-None-Match to get 304 Not Modified while it is current. Before the first publish, returns the empty snapshot (version 0).")
            .WithTags("Snapshot")
            .Produces<RulesSnapshot>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status304NotModified);

        return app;
    }

    // Serialized with RulesSnapshotJson.Options, the same options HttpRulesSnapshotSource reads with.
    public static async Task<Results<JsonHttpResult<RulesSnapshot>, StatusCodeHttpResult>> GetSnapshot(
        HttpContext httpContext,
        RulesContext context,
        CancellationToken cancellationToken)
    {
        var latestVersion = await context.RulesSnapshots.MaxAsync(s => (long?)s.Version, cancellationToken)
            ?? RulesSnapshot.Empty.Version;

        var etag = new EntityTagHeaderValue($"\"{latestVersion.ToString(CultureInfo.InvariantCulture)}\"");
        httpContext.Response.GetTypedHeaders().ETag = etag;

        if (MatchesIfNoneMatch(httpContext.Request, etag))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        var snapshot = latestVersion == RulesSnapshot.Empty.Version
            ? RulesSnapshot.Empty
            : (await context.RulesSnapshots
                .AsNoTracking()
                .SingleAsync(s => s.Version == latestVersion, cancellationToken))
                .ToSnapshot();

        return TypedResults.Json(snapshot, RulesSnapshotJson.Options);
    }

    private static bool MatchesIfNoneMatch(HttpRequest request, EntityTagHeaderValue etag)
        => request.GetTypedHeaders().IfNoneMatch is { Count: > 0 } ifNoneMatch
            && ifNoneMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(etag, useStrongComparison: false));
}
