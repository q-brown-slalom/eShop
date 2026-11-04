namespace eShop.BusinessRules.API.Services;

/// <summary>
/// Refuses to publish a price-affecting rule while clients still call create-order v1, because v1 orders are not
/// checked against the price the customer was shown. A publisher can override with a reason, which is audited.
/// If Ordering's usage cannot be read the publish is refused (fail closed, plan decision D3).
/// </summary>
public sealed class V1ClientsPublishGuard(IOrderingUsageClient usageClient, ILogger<V1ClientsPublishGuard> logger) : IPublishGuard
{
    public const int UsageWindowDays = 7;
    public const int MaxOverrideReasonLength = 500;

    public async Task<GuardResult> CheckAsync(IRuleType type, string? overrideReason, CancellationToken ct)
    {
        var reason = string.IsNullOrWhiteSpace(overrideReason) ? null : overrideReason.Trim();
        if (reason is { Length: > MaxOverrideReasonLength })
        {
            return new(false, false, PublishProblemTypes.InvalidOverrideReason)
            {
                Detail = $"The guard override reason must be at most {MaxOverrideReasonLength} characters.",
            };
        }

        if (!type.AffectsPrice)
        {
            return GuardResult.Allow;
        }

        long calls;
        try
        {
            calls = await usageClient.GetCreateOrderV1CallsAsync(UsageWindowDays, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not read create-order v1 usage from Ordering; refusing to publish rule type {RuleTypeId}", type.Id);
            return new(false, false, PublishProblemTypes.GuardUnavailable)
            {
                Detail = "Create-order v1 usage could not be read from Ordering, so price-affecting rules cannot be published right now.",
            };
        }

        if (calls == 0)
        {
            return GuardResult.Allow;
        }

        if (reason is null)
        {
            return new(false, false, PublishProblemTypes.V1ClientsActive)
            {
                Detail = $"Create order v1 was called {calls} times in the last {UsageWindowDays} days. v1 clients are not protected against price changes; publish with a guard override reason to proceed.",
            };
        }

        logger.LogWarning("Publish guard overridden for rule type {RuleTypeId} with {Calls} create-order v1 calls in the last {Days} days", type.Id, calls, UsageWindowDays);
        return new(true, true, null);
    }
}
