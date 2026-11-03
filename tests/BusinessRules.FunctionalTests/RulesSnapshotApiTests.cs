using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using eShop.BusinessRules.API.Infrastructure;
using eShop.BusinessRules.API.Model;
using eShop.BusinessRules.Client.Snapshot;
using eShop.BusinessRules.UnitTests;
using Microsoft.EntityFrameworkCore;

namespace eShop.BusinessRules.FunctionalTests;

[TestClass]
public sealed class RulesSnapshotApiTests
{
    private const string SnapshotUri = "api/rules/snapshot?api-version=1.0";
    private const string RuntimeScope = "businessrules";

    private HttpClient _httpClient = null!;

    public TestContext TestContext { get; set; } = null!;

    private static BusinessRulesApiFixture Fixture => BusinessRulesApiAssembly.Fixture;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _httpClient = Fixture.CreateClient();

        using var scope = Fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RulesContext>();
        await context.RulesSnapshots.ExecuteDeleteAsync(TestContext.CancellationToken);
    }

    [TestCleanup]
    public void Cleanup() => _httpClient.Dispose();

    [TestMethod]
    public async Task SnapshotReturnsEmptyVersionZeroBeforeFirstPublish()
    {
        var response = await SendAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("\"0\"", response.Headers.ETag?.Tag);
        var snapshot = await response.Content.ReadFromJsonAsync<RulesSnapshot>(RulesSnapshotJson.Options, TestContext.CancellationToken);
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(0, snapshot.Version);
        Assert.AreEqual(RulesSnapshot.Empty.PublishedAt, snapshot.PublishedAt);
        Assert.IsEmpty(snapshot.Rules);
    }

    [TestMethod]
    public async Task SnapshotReturnsLatestVersionWithEtag()
    {
        await AddSnapshotAsync(1, Rule(enabled: false));
        await AddSnapshotAsync(2, Rule(enabled: true));

        var response = await SendAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("\"2\"", response.Headers.ETag?.Tag);
        var snapshot = await response.Content.ReadFromJsonAsync<RulesSnapshot>(RulesSnapshotJson.Options, TestContext.CancellationToken);
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(2, snapshot.Version);
        Assert.IsTrue(snapshot.Rules.Single().Parameters.GetProperty("enabled").GetBoolean());
    }

    [TestMethod]
    public async Task SnapshotReturnsNotModifiedWhenEtagMatches()
    {
        await AddSnapshotAsync(3, Rule(enabled: true));

        var response = await SendAsync(ifNoneMatch: "\"3\"");

        Assert.AreEqual(HttpStatusCode.NotModified, response.StatusCode);
        Assert.AreEqual("\"3\"", response.Headers.ETag?.Tag);
        Assert.IsEmpty(await response.Content.ReadAsByteArrayAsync(TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task SnapshotReturnsNewSnapshotWhenEtagIsStale()
    {
        await AddSnapshotAsync(4, Rule(enabled: true));

        var response = await SendAsync(ifNoneMatch: "\"3\"");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("\"4\"", response.Headers.ETag?.Tag);
    }

    [TestMethod]
    public async Task SnapshotRequiresRuntimeScope()
    {
        var adminOnly = await SendAsync(scopes: "businessrules.admin", roles: "RulesPublisher");
        var customer = await SendAsync(scopes: "orders");
        var anonymous = await _httpClient.GetAsync(SnapshotUri, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Forbidden, adminOnly.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, customer.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [TestMethod]
    public async Task SnapshotRoundTripsThroughHttpRulesSnapshotSource()
    {
        var rule = Rule(enabled: true) with { EffectiveTo = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var publishedAt = new DateTimeOffset(2026, 11, 3, 12, 30, 0, TimeSpan.Zero);
        await AddSnapshotAsync(5, publishedAt, rule);

        using var client = Fixture.CreateDefaultClient(new RuntimeScopeHandler());
        var source = new HttpRulesSnapshotSource(client);

        var fetched = await source.FetchAsync(null, TestContext.CancellationToken);

        Assert.IsFalse(fetched.NotModified);
        Assert.IsNotNull(fetched.Snapshot);
        Assert.AreEqual(5, fetched.Snapshot.Version);
        Assert.AreEqual(publishedAt, fetched.Snapshot.PublishedAt);
        var fetchedRule = fetched.Snapshot.Rules.Single();
        Assert.AreEqual(rule.RuleTypeId, fetchedRule.RuleTypeId);
        Assert.AreEqual(rule.Scope, fetchedRule.Scope);
        Assert.AreEqual(rule.VersionNumber, fetchedRule.VersionNumber);
        Assert.AreEqual(rule.EffectiveFrom, fetchedRule.EffectiveFrom);
        Assert.AreEqual(rule.EffectiveTo, fetchedRule.EffectiveTo);
        Assert.IsTrue(fetchedRule.Parameters.GetProperty("enabled").GetBoolean());

        var refetched = await source.FetchAsync(5, TestContext.CancellationToken);

        Assert.IsTrue(refetched.NotModified);
        Assert.IsNull(refetched.Snapshot);
    }

    private static SnapshotRuleVersion Rule(bool enabled)
        => new(
            TestRuleType.TypeId,
            RuleScopes.Default,
            1,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            JsonSerializer.SerializeToElement(new { enabled }));

    private Task AddSnapshotAsync(long version, params SnapshotRuleVersion[] rules)
        => AddSnapshotAsync(version, DateTimeOffset.UtcNow, rules);

    private async Task AddSnapshotAsync(long version, DateTimeOffset publishedAt, params SnapshotRuleVersion[] rules)
    {
        using var scope = Fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RulesContext>();
        context.RulesSnapshots.Add(RulesSnapshotRecord.Create(version, publishedAt, rules));
        await context.SaveChangesAsync(TestContext.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(string scopes = RuntimeScope, string roles = "", string? ifNoneMatch = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, SnapshotUri);
        request.Headers.Add(TestAuthHandler.ScopesHeader, scopes);
        request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        request.Headers.Add(TestAuthHandler.SubjectHeader, "runtime-client");
        if (ifNoneMatch is not null)
        {
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(ifNoneMatch));
        }

        return await _httpClient.SendAsync(request, TestContext.CancellationToken);
    }

    /// <summary>Authenticates the client library's requests as a runtime (scope <c>businessrules</c>) caller.</summary>
    private sealed class RuntimeScopeHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add(TestAuthHandler.ScopesHeader, RuntimeScope);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
