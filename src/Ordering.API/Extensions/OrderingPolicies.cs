using Microsoft.AspNetCore.Authorization;

namespace eShop.Ordering.API.Extensions;

public static class OrderingPolicies
{
    public const string UsageReader = "Ordering.UsageReader";

    public const string ScopeClaim = "scope";
    public const string UsageScope = "ordering.usage";

    public static AuthorizationBuilder AddOrderingPolicies(this AuthorizationBuilder authorization) =>
        authorization
            .AddPolicy(UsageReader, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ScopeClaim, UsageScope));
}
