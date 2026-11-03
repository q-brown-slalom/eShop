using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace eShop.BusinessRules.API.Apis;

public static class RulesAdminApi
{
    public const int MaxPageSize = 100;

    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static IEndpointRouteBuilder MapRulesAdminApi(this IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("Rules")
            .MapGroup("api/rules")
            .HasApiVersion(1, 0)
            .RequireAuthorization(RulesPolicies.Editor);

        api.MapGet("/types", GetRuleTypes)
            .WithName("ListRuleTypes")
            .WithSummary("List rule types")
            .WithDescription("Get the rule types registered in code, with their parameter schema and built-in default.")
            .WithTags("Rules");

        api.MapGet("/versions", GetVersions)
            .WithName("ListRuleVersions")
            .WithSummary("List rule version history")
            .WithDescription("Get every version of a rule type in a scope, newest first.")
            .WithTags("Rules");

        api.MapPost("/versions", CreateDraft)
            .WithName("CreateRuleDraft")
            .WithSummary("Create a rule draft")
            .WithDescription("Create a draft version of a rule type with the next version number. Parameters are validated by the rule type.")
            .WithTags("Rules");

        api.MapPut("/versions/{id:guid}", UpdateDraft)
            .WithName("UpdateRuleDraft")
            .WithSummary("Update a rule draft")
            .WithDescription("Replace the parameters and effective window of a draft. Published and superseded versions, and drafts changed concurrently, cannot be edited (409).")
            .WithTags("Rules");

        api.MapPost("/versions/{id:guid}/publish", Publish)
            .RequireAuthorization(RulesPolicies.Publisher)
            .WithName("PublishRuleVersion")
            .WithSummary("Publish a rule draft")
            .WithDescription("Publish a draft into a new rules snapshot version and notify consumers. Non-drafts and concurrent publishes return 409; the publish guard may refuse the publish.")
            .WithTags("Rules");

        api.MapGet("/audit", GetAudit)
            .WithName("ListRuleAudit")
            .WithSummary("List the rules audit log")
            .WithDescription("Get a paginated list of rule changes, newest first.")
            .WithTags("Audit");

        return app;
    }

    public static Ok<IReadOnlyList<RuleTypeDto>> GetRuleTypes(RuleTypeRegistry registry)
        => TypedResults.Ok<IReadOnlyList<RuleTypeDto>>(registry.All
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .Select(RuleTypeDto.From)
            .ToList());

    public static async Task<Ok<IReadOnlyList<RuleVersionDto>>> GetVersions(
        RulesContext context,
        string ruleTypeId,
        CancellationToken cancellationToken,
        string scope = RuleScopes.Default)
    {
        var versions = await context.RuleVersions
            .AsNoTracking()
            .Where(v => v.RuleTypeId == ruleTypeId && v.Scope == scope)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<RuleVersionDto>>(versions.Select(RuleVersionDto.From).ToList());
    }

    public static async Task<Results<Created<RuleVersionDto>, ValidationProblem>> CreateDraft(
        CreateDraftRequest request,
        ClaimsPrincipal user,
        RulesContext context,
        IValidator<CreateDraftRequest> validator,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        var now = timeProvider.GetUtcNow();
        var actor = GetActor(user);

        var latestVersionNumber = await context.RuleVersions
            .Where(v => v.RuleTypeId == request.RuleTypeId && v.Scope == request.Scope)
            .MaxAsync(v => (int?)v.VersionNumber, cancellationToken) ?? 0;

        var version = RuleVersion.CreateDraft(
            request.RuleTypeId,
            request.Scope,
            latestVersionNumber + 1,
            JsonSerializer.SerializeToDocument(request.Parameters),
            request.EffectiveFrom,
            request.EffectiveTo,
            actor,
            now);

        context.RuleVersions.Add(version);
        context.AuditEntries.Add(new AuditEntry(
            now, actor, AuditAction.DraftCreated, version.RuleTypeId, version.Scope, version.Id,
            beforeJson: null, afterJson: ToAuditJson(version)));

        await context.SaveChangesAsync(cancellationToken);

        loggerFactory.CreateLogger(typeof(RulesAdminApi)).LogInformation(
            "Rule draft {RuleTypeId} v{VersionNumber} created", version.RuleTypeId, version.VersionNumber);

        return TypedResults.Created($"/api/rules/versions/{version.Id}", RuleVersionDto.From(version));
    }

    public static async Task<Results<Ok<RuleVersionDto>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> UpdateDraft(
        Guid id,
        UpdateDraftRequest request,
        ClaimsPrincipal user,
        RulesContext context,
        IValidator<UpdateDraftRequest> validator,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var version = await context.RuleVersions.SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (version is null)
        {
            return TypedResults.NotFound();
        }

        if (version.Status != RuleVersionStatus.Draft)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Rule version is not a draft",
                Detail = $"Rule version {id} is {version.Status}; only drafts can be edited."
            });
        }

        var validationContext = new ValidationContext<UpdateDraftRequest>(request);
        validationContext.RootContextData[UpdateDraftRequestValidator.RuleTypeIdKey] = version.RuleTypeId;
        var validation = await validator.ValidateAsync(validationContext, cancellationToken);
        if (!validation.IsValid)
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        var before = ToAuditJson(version);
        version.UpdateDraft(JsonSerializer.SerializeToDocument(request.Parameters), request.EffectiveFrom, request.EffectiveTo);

        context.AuditEntries.Add(new AuditEntry(
            timeProvider.GetUtcNow(), GetActor(user), AuditAction.DraftUpdated, version.RuleTypeId, version.Scope, version.Id,
            beforeJson: before, afterJson: ToAuditJson(version)));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Rule version was changed concurrently",
                Detail = $"Rule version {id} was changed by another request; reload it and try again."
            });
        }

        loggerFactory.CreateLogger(typeof(RulesAdminApi)).LogInformation(
            "Rule draft {RuleTypeId} v{VersionNumber} updated", version.RuleTypeId, version.VersionNumber);

        return TypedResults.Ok(RuleVersionDto.From(version));
    }

    public static async Task<Results<Ok<PublishResultDto>, NotFound, Conflict<ProblemDetails>, ForbidHttpResult, ProblemHttpResult>> Publish(
        Guid id,
        PublishRequest? request,
        ClaimsPrincipal user,
        RulePublishingService publishingService,
        CancellationToken cancellationToken)
    {
        var outcome = await publishingService.PublishAsync(id, GetActor(user), request?.GuardOverrideReason, cancellationToken);

        return outcome.Kind switch
        {
            PublishOutcomeKind.Published => TypedResults.Ok(new PublishResultDto(id, outcome.SnapshotVersion)),
            PublishOutcomeKind.NotFound => TypedResults.NotFound(),
            PublishOutcomeKind.GuardRejected when outcome.ProblemType == PublishProblemTypes.GuardUnavailable => TypedResults.Problem(
                type: outcome.ProblemType,
                title: "Publish guard unavailable",
                detail: "The publish guard could not check this publish, so it was refused.",
                statusCode: StatusCodes.Status503ServiceUnavailable),
            PublishOutcomeKind.GuardRejected => TypedResults.Conflict(new ProblemDetails
            {
                Type = outcome.ProblemType,
                Status = StatusCodes.Status409Conflict,
                Title = "Publish refused by the publish guard",
            }),
            _ => TypedResults.Conflict(new ProblemDetails
            {
                Type = outcome.ProblemType,
                Status = StatusCodes.Status409Conflict,
                Title = outcome.Title,
                Detail = outcome.Detail,
            }),
        };
    }

    public static async Task<Ok<PaginatedItems<AuditEntryDto>>> GetAudit(
        RulesContext context,
        CancellationToken cancellationToken,
        int pageIndex = 0,
        int pageSize = 10)
    {
        pageIndex = Math.Max(pageIndex, 0);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var count = await context.AuditEntries.LongCountAsync(cancellationToken);
        var entries = await context.AuditEntries
            .AsNoTracking()
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PaginatedItems<AuditEntryDto>(pageIndex, pageSize, count, entries.Select(AuditEntryDto.From).ToList()));
    }

    // The Editor and Publisher policies require a "sub" claim, so an authorized caller always has one.
    private static string GetActor(ClaimsPrincipal user)
        => user.GetUserId() ?? throw new InvalidOperationException("The authenticated user has no 'sub' claim.");

    internal static string ToAuditJson(RuleVersion version)
        => JsonSerializer.Serialize(RuleVersionDto.From(version), AuditJsonOptions);
}
