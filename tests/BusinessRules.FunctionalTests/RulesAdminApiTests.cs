using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Asp.Versioning.Http;
using eShop.BusinessRules.API.Apis;
using eShop.BusinessRules.API.Infrastructure;
using eShop.BusinessRules.API.Model;
using eShop.BusinessRules.UnitTests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eShop.BusinessRules.FunctionalTests;

[TestClass]
public sealed class RulesAdminApiTests
{
    private const string AdminScope = "businessrules.admin";
    private const string EditorRole = "RulesEditor";
    private const string EditorSub = "editor-1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly DateTimeOffset Tomorrow = new(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero);

    private HttpClient _httpClient = null!;

    public TestContext TestContext { get; set; } = null!;

    private static BusinessRulesApiFixture Fixture => BusinessRulesApiAssembly.Fixture;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _httpClient = Fixture.CreateDefaultClient(new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(1.0)));

        using var scope = Fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RulesContext>();
        await context.AuditEntries.ExecuteDeleteAsync(TestContext.CancellationToken);
        await context.RuleVersions.ExecuteDeleteAsync(TestContext.CancellationToken);
    }

    [TestCleanup]
    public void Cleanup() => _httpClient.Dispose();

    [TestMethod]
    public async Task GetTypesReturnsRegisteredRuleTypes()
    {
        var response = await SendAsync(HttpMethod.Get, "api/rules/types");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var types = await response.Content.ReadFromJsonAsync<List<RuleTypeDto>>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(types);
        var type = types.Single(t => t.Id == TestRuleType.TypeId);
        Assert.AreEqual("Test rule", type.Name);
        Assert.IsFalse(type.Default.GetProperty("enabled").GetBoolean());
    }

    [TestMethod]
    public async Task CreateDraftWithValidParametersReturnsCreatedAndAudits()
    {
        var response = await CreateDraftAsync("""{"enabled":true}""");

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var version = await ReadVersionAsync(response);
        Assert.AreEqual(1, version.VersionNumber);
        Assert.AreEqual(TestRuleType.TypeId, version.RuleTypeId);
        Assert.AreEqual(RuleScopes.Default, version.Scope);
        Assert.AreEqual(RuleVersionStatus.Draft, version.Status);
        Assert.AreEqual(EditorSub, version.CreatedBy);
        Assert.IsTrue(version.Parameters.GetProperty("enabled").GetBoolean());
        Assert.AreEqual(Tomorrow, version.EffectiveFrom);
        Assert.IsNotNull(response.Headers.Location);
        Assert.Contains(version.Id.ToString(), response.Headers.Location.ToString());

        var audit = await GetAuditAsync();
        var entry = audit.Data.Single();
        Assert.AreEqual(AuditAction.DraftCreated, entry.Action);
        Assert.AreEqual(EditorSub, entry.ActorId);
        Assert.AreEqual(version.Id, entry.RuleVersionId);
        Assert.AreEqual(TestRuleType.TypeId, entry.RuleTypeId);
        Assert.IsNull(entry.BeforeJson);
        Assert.IsNotNull(entry.AfterJson);
    }

    [TestMethod]
    public async Task CreateDraftAssignsNextVersionNumber()
    {
        await CreateDraftAsync("""{"enabled":true}""");

        var response = await CreateDraftAsync("""{"enabled":false}""");

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual(2, (await ReadVersionAsync(response)).VersionNumber);
    }

    [TestMethod]
    public async Task CreateDraftWithInvalidParametersReturnsValidationProblem()
    {
        var response = await CreateDraftAsync("""{"enabled":"yes"}""");

        var problem = await AssertValidationProblemAsync(response);
        CollectionAssert.Contains(problem.Errors["Parameters"], "'enabled' must be a boolean.");
        Assert.AreEqual(0, (await GetAuditAsync()).Count);
    }

    [TestMethod]
    public async Task CreateDraftForUnknownRuleTypeReturnsValidationProblem()
    {
        var response = await CreateDraftAsync("""{"enabled":true}""", ruleTypeId: "ZZZ-999");

        var problem = await AssertValidationProblemAsync(response);
        Assert.IsTrue(problem.Errors.ContainsKey("RuleTypeId"));
    }

    [TestMethod]
    public async Task CreateDraftForNonDefaultScopeReturnsValidationProblem()
    {
        var response = await CreateDraftAsync("""{"enabled":true}""", scope: "offering-42");

        var problem = await AssertValidationProblemAsync(response);
        Assert.IsTrue(problem.Errors.ContainsKey("Scope"));
    }

    [TestMethod]
    public async Task CreateDraftWithEffectiveToNotAfterEffectiveFromReturnsValidationProblem()
    {
        var response = await CreateDraftAsync("""{"enabled":true}""", effectiveTo: Tomorrow);

        var problem = await AssertValidationProblemAsync(response);
        Assert.IsTrue(problem.Errors.ContainsKey("EffectiveTo"));
    }

    [TestMethod]
    public async Task UpdateDraftReturnsUpdatedVersionAndAudits()
    {
        var created = await ReadVersionAsync(await CreateDraftAsync("""{"enabled":false}"""));

        var response = await SendAsync(HttpMethod.Put, $"api/rules/versions/{created.Id}",
            new { parameters = new { enabled = true }, effectiveFrom = Tomorrow.AddDays(1), effectiveTo = Tomorrow.AddDays(2) });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadVersionAsync(response);
        Assert.IsTrue(updated.Parameters.GetProperty("enabled").GetBoolean());
        Assert.AreEqual(Tomorrow.AddDays(1), updated.EffectiveFrom);
        Assert.AreEqual(Tomorrow.AddDays(2), updated.EffectiveTo);

        var entry = (await GetAuditAsync()).Data.First();
        Assert.AreEqual(AuditAction.DraftUpdated, entry.Action);
        Assert.IsNotNull(entry.BeforeJson);
        Assert.IsNotNull(entry.AfterJson);
    }

    [TestMethod]
    public async Task UpdateDraftWithInvalidParametersReturnsValidationProblem()
    {
        var created = await ReadVersionAsync(await CreateDraftAsync("""{"enabled":false}"""));

        var response = await SendAsync(HttpMethod.Put, $"api/rules/versions/{created.Id}",
            new { parameters = new { enabled = 1 }, effectiveFrom = Tomorrow });

        var problem = await AssertValidationProblemAsync(response);
        CollectionAssert.Contains(problem.Errors["Parameters"], "'enabled' must be a boolean.");
    }

    [TestMethod]
    public async Task UpdateUnknownVersionReturnsNotFound()
    {
        var response = await SendAsync(HttpMethod.Put, $"api/rules/versions/{Guid.NewGuid()}",
            new { parameters = new { enabled = true }, effectiveFrom = Tomorrow });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdatePublishedVersionReturnsConflict()
    {
        var created = await ReadVersionAsync(await CreateDraftAsync("""{"enabled":false}"""));
        using (var scope = Fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RulesContext>();
            var version = await context.RuleVersions.SingleAsync(v => v.Id == created.Id, TestContext.CancellationToken);
            version.Publish("publisher-1", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync(TestContext.CancellationToken);
        }

        var response = await SendAsync(HttpMethod.Put, $"api/rules/versions/{created.Id}",
            new { parameters = new { enabled = true }, effectiveFrom = Tomorrow });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions, TestContext.CancellationToken);
        Assert.AreEqual(409, problem?.Status);
    }

    [TestMethod]
    public async Task UpdateDraftPublishedConcurrentlyReturnsConflictAndKeepsPublishedVersion()
    {
        var created = await ReadVersionAsync(await CreateDraftAsync("""{"enabled":false}"""));

        using var scope = Fixture.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<RulesContext>().Database.GetConnectionString();
        // Publishes the row after UpdateDraft has loaded it and passed its draft check, just before its save.
        var publishBeforeSave = new PublishBeforeSaveInterceptor(created.Id, connectionString!);
        await using var context = new RulesContext(new DbContextOptionsBuilder<RulesContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(publishBeforeSave)
            .Options);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", EditorSub)], "test"));

        var result = await RulesAdminApi.UpdateDraft(
            created.Id,
            new UpdateDraftRequest(JsonDocument.Parse("""{"enabled":true}""").RootElement, Tomorrow.AddDays(5), null),
            user,
            context,
            scope.ServiceProvider.GetRequiredService<FluentValidation.IValidator<UpdateDraftRequest>>(),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            TestContext.CancellationToken);

        Assert.IsTrue(publishBeforeSave.Published);
        var conflict = result.Result as Microsoft.AspNetCore.Http.HttpResults.Conflict<ProblemDetails>;
        Assert.IsNotNull(conflict, $"Expected 409 Conflict but got {result.Result.GetType().Name}.");
        Assert.AreEqual(409, conflict.Value?.Status);

        var stored = await scope.ServiceProvider.GetRequiredService<RulesContext>().RuleVersions
            .AsNoTracking().SingleAsync(v => v.Id == created.Id, TestContext.CancellationToken);
        Assert.AreEqual(RuleVersionStatus.Published, stored.Status);
        Assert.IsFalse(stored.Parameters.RootElement.GetProperty("enabled").GetBoolean());
        Assert.IsFalse((await GetAuditAsync()).Data.Any(a => a.Action == AuditAction.DraftUpdated));
    }

    private sealed class PublishBeforeSaveInterceptor(Guid versionId, string connectionString) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Published { get; private set; }

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (!Published)
            {
                await using var other = new RulesContext(new DbContextOptionsBuilder<RulesContext>().UseNpgsql(connectionString).Options);
                var version = await other.RuleVersions.SingleAsync(v => v.Id == versionId, cancellationToken);
                version.Publish("publisher-1", DateTimeOffset.UtcNow);
                await other.SaveChangesAsync(cancellationToken);
                Published = true;
            }

            return result;
        }
    }

    [TestMethod]
    public async Task GetVersionsReturnsHistoryNewestFirst()
    {
        await CreateDraftAsync("""{"enabled":false}""");
        await CreateDraftAsync("""{"enabled":true}""");

        var response = await SendAsync(HttpMethod.Get, $"api/rules/versions?ruleTypeId={TestRuleType.TypeId}&scope={RuleScopes.Default}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var versions = await response.Content.ReadFromJsonAsync<List<RuleVersionDto>>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(versions);
        CollectionAssert.AreEqual(new[] { 2, 1 }, versions.Select(v => v.VersionNumber).ToArray());
    }

    [TestMethod]
    public async Task GetAuditPagesNewestFirst()
    {
        for (var i = 0; i < 3; i++)
        {
            await CreateDraftAsync("""{"enabled":true}""");
        }

        var page = await GetAuditAsync(pageIndex: 1, pageSize: 2);

        Assert.AreEqual(3, page.Count);
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(2, page.PageSize);
        var entry = page.Data.Single();
        Assert.AreEqual(AuditAction.DraftCreated, entry.Action);
        Assert.IsNotNull(entry.AfterJson);
        Assert.Contains("\"versionNumber\":1", entry.AfterJson);
    }

    [TestMethod]
    [DataRow("GET", "api/rules/types")]
    [DataRow("GET", "api/rules/versions?ruleTypeId=TST-001&scope=default")]
    [DataRow("POST", "api/rules/versions")]
    [DataRow("PUT", "api/rules/versions/8f5a3c1e-0000-4000-8000-000000000001")]
    [DataRow("GET", "api/rules/audit")]
    public async Task AdminEndpointsRequireAdminScope(string method, string uri)
    {
        var response = await SendAsync(new HttpMethod(method), uri, ValidCreateBody(), scopes: "businessrules", roles: EditorRole);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminEndpointsRequireEditorRole()
    {
        var response = await SendAsync(HttpMethod.Post, "api/rules/versions", ValidCreateBody(), roles: "");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminEndpointsRequireSubject()
    {
        var response = await SendAsync(HttpMethod.Post, "api/rules/versions", ValidCreateBody(), sub: "");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task CustomerTokenIsRejected()
    {
        var response = await SendAsync(HttpMethod.Post, "api/rules/versions", ValidCreateBody(), scopes: "orders", roles: "");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static object ValidCreateBody(
        string parametersJson = """{"enabled":true}""",
        string ruleTypeId = TestRuleType.TypeId,
        string scope = RuleScopes.Default,
        DateTimeOffset? effectiveTo = null)
        => new
        {
            ruleTypeId,
            scope,
            parameters = JsonDocument.Parse(parametersJson).RootElement,
            effectiveFrom = Tomorrow,
            effectiveTo,
        };

    private Task<HttpResponseMessage> CreateDraftAsync(
        string parametersJson,
        string ruleTypeId = TestRuleType.TypeId,
        string scope = RuleScopes.Default,
        DateTimeOffset? effectiveTo = null)
        => SendAsync(HttpMethod.Post, "api/rules/versions", ValidCreateBody(parametersJson, ruleTypeId, scope, effectiveTo));

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string uri,
        object? body = null,
        string scopes = AdminScope,
        string roles = EditorRole,
        string sub = EditorSub)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null && method != HttpMethod.Get)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        request.Headers.Add(TestAuthHandler.ScopesHeader, scopes);
        request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        request.Headers.Add(TestAuthHandler.SubjectHeader, sub);
        return await _httpClient.SendAsync(request, TestContext.CancellationToken);
    }

    private async Task<RuleVersionDto> ReadVersionAsync(HttpResponseMessage response)
    {
        var version = await response.Content.ReadFromJsonAsync<RuleVersionDto>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(version);
        return version;
    }

    private async Task<PaginatedItems<AuditEntryDto>> GetAuditAsync(int pageIndex = 0, int pageSize = 10)
    {
        var response = await SendAsync(HttpMethod.Get, $"api/rules/audit?pageIndex={pageIndex}&pageSize={pageSize}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PaginatedItems<AuditEntryDto>>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(page);
        return page;
    }

    private async Task<HttpValidationProblemDetails> AssertValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(problem);
        return problem;
    }
}
