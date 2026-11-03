namespace eShop.BusinessRules.API.Services;

/// <summary>Decides whether a rule version of <paramref name="type"/> may be published right now.</summary>
public interface IPublishGuard
{
    Task<GuardResult> CheckAsync(IRuleType type, string? overrideReason, CancellationToken ct);
}

/// <param name="Allowed">Whether the publish may go ahead.</param>
/// <param name="Overridden">Whether it was allowed only because of the caller's override reason.</param>
/// <param name="ProblemType">The ProblemDetails type explaining why the publish was refused.</param>
public record GuardResult(bool Allowed, bool Overridden, string? ProblemType)
{
    public static GuardResult Allow { get; } = new(true, false, null);
}

public static class PublishProblemTypes
{
    public const string ConcurrentPublish = "https://eshop/problems/concurrent-publish";
    public const string V1ClientsActive = "https://eshop/problems/v1-clients-active";
    public const string GuardUnavailable = "https://eshop/problems/publish-guard-unavailable";
}
