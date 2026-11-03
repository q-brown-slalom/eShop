using Microsoft.AspNetCore.Authorization;

namespace eShop.BusinessRules.API.Authorization;

public static class RulesPolicies
{
    public const string Runtime = "BusinessRules.Runtime";
    public const string Editor = "BusinessRules.Editor";
    public const string Publisher = "BusinessRules.Publisher";

    public const string ScopeClaim = "scope";
    public const string SubjectClaim = "sub";
    public const string RuntimeScope = "businessrules";
    public const string AdminScope = "businessrules.admin";

    public const string EditorRole = "RulesEditor";
    public const string PublisherRole = "RulesPublisher";

    public static AuthorizationBuilder AddRulesPolicies(this AuthorizationBuilder authorization) =>
        authorization
            .AddPolicy(Runtime, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ScopeClaim, RuntimeScope))
            // Admin changes are audited against the user's "sub", so it is required.
            .AddPolicy(Editor, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ScopeClaim, AdminScope)
                .RequireClaim(SubjectClaim)
                .RequireRole(EditorRole, PublisherRole))
            .AddPolicy(Publisher, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ScopeClaim, AdminScope)
                .RequireClaim(SubjectClaim)
                .RequireRole(PublisherRole));
}
