namespace eShop.BusinessRules.API.Services;

/// <summary>Allows every publish. Placeholder until the v1-clients guard replaces it.</summary>
public sealed class AllowAllPublishGuard : IPublishGuard
{
    public Task<GuardResult> CheckAsync(IRuleType type, string? overrideReason, CancellationToken ct)
        => Task.FromResult(GuardResult.Allow);
}
