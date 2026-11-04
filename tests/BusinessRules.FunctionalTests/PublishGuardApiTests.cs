using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Asp.Versioning.Http;
using eShop.BusinessRules.API.Apis;
using eShop.BusinessRules.API.Infrastructure;
using eShop.BusinessRules.API.Model;
using eShop.BusinessRules.UnitTests;
using eShop.IntegrationEventLogEF;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eShop.BusinessRules.FunctionalTests;

[TestClass]
public sealed class PublishGuardApiTests
{
    private const string AdminScope = "businessrules.admin";
    private const string EditorRole = "RulesEditor";
    private const string PublisherRole = "RulesPublisher";
    private const string EditorSub = "editor-1";
    private const string PublisherSub = "publisher-1";

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
        Fixture.OrderingUsage.Reset();
        _httpClient = Fixture.CreateDefaultClient(new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(1.0)));

        using var scope = Fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RulesContext>();
        await context.AuditEntries.ExecuteDeleteAsync(TestContext.CancellationToken);
        await context.RuleVersions.ExecuteDeleteAsync(TestContext.CancellationToken);
        await context.RulesSnapshots.ExecuteDeleteAsync(TestContext.CancellationToken);
        await context.Set<IntegrationEventLogEntry>().ExecuteDeleteAsync(TestContext.CancellationToken);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Fixture.OrderingUsage.Reset();
        _httpClient.Dispose();
    }

    [TestMethod]
    public async Task PublishPriceRuleWithV1TrafficReturnsConflict()
    {
        Fixture.OrderingUsage.Calls = 17;
        var draft = await CreateDraftAsync();

        var response = await PublishAsync(draft.Id, overrideReason: null);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(problem);
        Assert.AreEqual("https://eshop/problems/v1-clients-active", problem.Type);
        Assert.AreEqual(409, problem.Status);
        Assert.IsNotNull(problem.Detail);
        Assert.Contains("17", problem.Detail);

        using var scope = Fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RulesContext>();
        Assert.IsFalse(await context.RulesSnapshots.AnyAsync(TestContext.CancellationToken));
        Assert.AreEqual(RuleVersionStatus.Draft, (await context.RuleVersions.SingleAsync(v => v.Id == draft.Id, TestContext.CancellationToken)).Status);
    }

    [TestMethod]
    public async Task PublishPriceRuleWithOverrideReasonAuditsGuardOverride()
    {
        const string reason = "Last v1 client retired; remaining calls are a smoke test.";
        Fixture.OrderingUsage.Calls = 4;
        var draft = await CreateDraftAsync();

        var response = await PublishAsync(draft.Id, reason);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        using var scope = Fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RulesContext>();
        var overridden = await context.AuditEntries.SingleAsync(a => a.Action == AuditAction.GuardOverridden, TestContext.CancellationToken);
        Assert.AreEqual(reason, overridden.Reason);
        Assert.AreEqual(PublisherSub, overridden.ActorId);
        Assert.AreEqual(draft.Id, overridden.RuleVersionId);
        Assert.AreEqual(TestPriceRuleType.TypeId, overridden.RuleTypeId);
        Assert.IsTrue(await context.AuditEntries.AnyAsync(a => a.Action == AuditAction.Published && a.RuleVersionId == draft.Id, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task PublishPriceRuleWhenUsageUnavailableReturnsServiceUnavailable()
    {
        Fixture.OrderingUsage.Failure = new HttpRequestException("ordering-api unreachable");
        var draft = await CreateDraftAsync();

        var response = await PublishAsync(draft.Id, overrideReason: "anything");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(problem);
        Assert.AreEqual(503, problem.Status);

        using var scope = Fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RulesContext>();
        Assert.IsFalse(await context.RulesSnapshots.AnyAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task PublishWithOverrideReasonLongerThan500CharsReturnsBadRequest()
    {
        Fixture.OrderingUsage.Calls = 4;
        var draft = await CreateDraftAsync();

        var response = await PublishAsync(draft.Id, new string('x', 501));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(problem);
        Assert.AreEqual(400, problem.Status);
    }

    private async Task<RuleVersionDto> CreateDraftAsync()
    {
        var body = new
        {
            ruleTypeId = TestPriceRuleType.TypeId,
            scope = RuleScopes.Default,
            parameters = JsonDocument.Parse("""{"enabled":true}""").RootElement,
            effectiveFrom = Tomorrow,
            effectiveTo = (DateTimeOffset?)null,
        };

        var response = await SendAsync("api/rules/versions", body, EditorRole, EditorSub);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var version = await response.Content.ReadFromJsonAsync<RuleVersionDto>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(version);
        return version;
    }

    private Task<HttpResponseMessage> PublishAsync(Guid id, string? overrideReason)
        => SendAsync($"api/rules/versions/{id}/publish", new PublishRequest(overrideReason), PublisherRole, PublisherSub);

    private async Task<HttpResponseMessage> SendAsync(string uri, object body, string roles, string sub)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };

        request.Headers.Add(TestAuthHandler.ScopesHeader, AdminScope);
        request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        request.Headers.Add(TestAuthHandler.SubjectHeader, sub);
        return await _httpClient.SendAsync(request, TestContext.CancellationToken);
    }
}
