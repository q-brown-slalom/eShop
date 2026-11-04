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
public sealed class PublishApiTests
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
        _httpClient = Fixture.CreateDefaultClient(new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(1.0)));

        using var scope = Fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RulesContext>();
        await context.AuditEntries.ExecuteDeleteAsync(TestContext.CancellationToken);
        await context.RuleVersions.ExecuteDeleteAsync(TestContext.CancellationToken);
        await context.RulesSnapshots.ExecuteDeleteAsync(TestContext.CancellationToken);
        await context.Set<IntegrationEventLogEntry>().ExecuteDeleteAsync(TestContext.CancellationToken);
    }

    [TestCleanup]
    public void Cleanup() => _httpClient.Dispose();

    [TestMethod]
    public async Task PublishCreatesNextSnapshotVersionAndOutboxEvent()
    {
        var first = await CreateDraftAsync();
        Assert.AreEqual(1, (await PublishOkAsync(first.Id)).SnapshotVersion);
        var second = await CreateDraftAsync("""{"enabled":false}""");

        var result = await PublishOkAsync(second.Id);

        Assert.AreEqual(second.Id, result.RuleVersionId);
        Assert.AreEqual(2, result.SnapshotVersion);

        using var scope = Fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RulesContext>();

        var snapshot = (await context.RulesSnapshots.SingleAsync(s => s.Version == 2, TestContext.CancellationToken)).ToSnapshot();
        CollectionAssert.AreEqual(new[] { 1, 2 }, snapshot.Rules.Select(r => r.VersionNumber).ToArray());

        var published = await context.RuleVersions.SingleAsync(v => v.Id == second.Id, TestContext.CancellationToken);
        Assert.AreEqual(RuleVersionStatus.Published, published.Status);
        Assert.AreEqual(PublisherSub, published.PublishedBy);

        var events = await context.Set<IntegrationEventLogEntry>()
            .OrderBy(e => e.CreationTime)
            .ToListAsync(TestContext.CancellationToken);
        Assert.HasCount(2, events);
        foreach (var entry in events)
        {
            Assert.EndsWith("BusinessRulesSnapshotPublishedIntegrationEvent", entry.EventTypeName);
            // The event is published to the bus only after the transaction commits.
            Assert.AreNotEqual(EventStateEnum.NotPublished, entry.State);
        }

        var versions = events.Select(e => JsonDocument.Parse(e.Content).RootElement.GetProperty("SnapshotVersion").GetInt64()).ToArray();
        CollectionAssert.AreEquivalent(new long[] { 1, 2 }, versions);

        var audit = await context.AuditEntries
            .Where(a => a.Action == AuditAction.Published)
            .ToListAsync(TestContext.CancellationToken);
        Assert.HasCount(2, audit);
        var entryForSecond = audit.Single(a => a.RuleVersionId == second.Id);
        Assert.AreEqual(PublisherSub, entryForSecond.ActorId);
        Assert.IsNotNull(entryForSecond.BeforeJson);
        Assert.IsNotNull(entryForSecond.AfterJson);
    }

    [TestMethod]
    public async Task EditorCannotPublish()
    {
        var draft = await CreateDraftAsync();

        var response = await PublishAsync(draft.Id, roles: EditorRole, sub: EditorSub);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = Fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RulesContext>();
        Assert.IsFalse(await context.RulesSnapshots.AnyAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task PublisherCanPublishOwnDraft()
    {
        var draft = await CreateDraftAsync(roles: PublisherRole, sub: PublisherSub);
        Assert.AreEqual(PublisherSub, draft.CreatedBy);

        var result = await PublishOkAsync(draft.Id);

        Assert.AreEqual(1, result.SnapshotVersion);
    }

    [TestMethod]
    public async Task PublishingNonDraftReturnsConflict()
    {
        var draft = await CreateDraftAsync();
        await PublishOkAsync(draft.Id);

        var response = await PublishAsync(draft.Id);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(problem);
        Assert.AreEqual(409, problem.Status);
        Assert.AreEqual("Rule version is not a draft", problem.Title);
    }

    [TestMethod]
    public async Task PublishingUnknownVersionReturnsNotFound()
    {
        var response = await PublishAsync(Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ConcurrentPublishProducesDistinctSnapshotVersions()
    {
        var snapshotVersions = new List<long>();

        for (var round = 0; round < 5; round++)
        {
            var first = await CreateDraftAsync();
            var second = await CreateDraftAsync();

            var responses = await Task.WhenAll(PublishAsync(first.Id), PublishAsync(second.Id));

            var conflicts = 0;
            foreach (var response in responses)
            {
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    conflicts++;
                    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions, TestContext.CancellationToken);
                    Assert.IsNotNull(problem);
                    Assert.EndsWith("concurrent-publish", problem.Type);
                    continue;
                }

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<PublishResultDto>(JsonOptions, TestContext.CancellationToken);
                Assert.IsNotNull(result);
                snapshotVersions.Add(result.SnapshotVersion);
            }

            Assert.IsLessThanOrEqualTo(1, conflicts);
        }

        CollectionAssert.AreEqual(
            Enumerable.Range(1, snapshotVersions.Count).Select(v => (long)v).ToArray(),
            snapshotVersions.Order().ToArray());

        using var scope = Fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RulesContext>();
        Assert.AreEqual(snapshotVersions.Count, await context.RulesSnapshots.CountAsync(TestContext.CancellationToken));
        Assert.AreEqual(snapshotVersions.Count, await context.RuleVersions.CountAsync(v => v.Status == RuleVersionStatus.Published, TestContext.CancellationToken));
        Assert.AreEqual(snapshotVersions.Count, await context.Set<IntegrationEventLogEntry>().CountAsync(TestContext.CancellationToken));

        // Every snapshot must contain every version published before it, so no publish is lost.
        var latest = (await context.RulesSnapshots.SingleAsync(s => s.Version == snapshotVersions.Count, TestContext.CancellationToken)).ToSnapshot();
        Assert.HasCount(snapshotVersions.Count, latest.Rules);
    }

    private async Task<RuleVersionDto> CreateDraftAsync(
        string parametersJson = """{"enabled":true}""",
        string roles = EditorRole,
        string sub = EditorSub)
    {
        var body = new
        {
            ruleTypeId = TestRuleType.TypeId,
            scope = RuleScopes.Default,
            parameters = JsonDocument.Parse(parametersJson).RootElement,
            effectiveFrom = Tomorrow,
            effectiveTo = (DateTimeOffset?)null,
        };

        var response = await SendAsync(HttpMethod.Post, "api/rules/versions", body, roles, sub);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var version = await response.Content.ReadFromJsonAsync<RuleVersionDto>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(version);
        return version;
    }

    private async Task<PublishResultDto> PublishOkAsync(Guid id)
    {
        var response = await PublishAsync(id);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        var result = await response.Content.ReadFromJsonAsync<PublishResultDto>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(result);
        return result;
    }

    private Task<HttpResponseMessage> PublishAsync(Guid id, string roles = PublisherRole, string sub = PublisherSub)
        => SendAsync(HttpMethod.Post, $"api/rules/versions/{id}/publish", new PublishRequest(null), roles, sub);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, object? body, string roles, string sub)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        request.Headers.Add(TestAuthHandler.ScopesHeader, AdminScope);
        request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        request.Headers.Add(TestAuthHandler.SubjectHeader, sub);
        return await _httpClient.SendAsync(request, TestContext.CancellationToken);
    }
}
