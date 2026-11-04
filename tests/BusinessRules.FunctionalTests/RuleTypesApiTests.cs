using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Asp.Versioning.Http;
using eShop.BusinessRules.API.Apis;
using eShop.BusinessRules.API.Infrastructure;
using eShop.BusinessRules.API.Model;
using eShop.IntegrationEventLogEF;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace eShop.BusinessRules.FunctionalTests;

[TestClass]
public sealed class RuleTypesApiTests
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
    public async Task ListRuleTypesReturnsFiveProductionTypes()
    {
        using var request = NewRequest(HttpMethod.Get, "api/rules/types", EditorRole, EditorSub);
        var response = await _httpClient.SendAsync(request, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var types = await response.Content.ReadFromJsonAsync<List<RuleTypeDto>>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(types);
        string[] productionIds = ["PRC-001", "PRM-001", "SHP-001", "ORD-001", "ORD-013"];
        var production = types.Where(t => productionIds.Contains(t.Id)).ToList();
        CollectionAssert.AreEquivalent(productionIds, production.Select(t => t.Id).ToList());
        CollectionAssert.AreEquivalent(
            new[] { "PRC-001", "PRM-001", "SHP-001" },
            production.Where(t => t.AffectsPrice).Select(t => t.Id).ToList());
        Assert.AreEqual(1, production.Single(t => t.Id == "ORD-001").Default.GetProperty("minutes").GetInt32());
    }

    [TestMethod]
    public async Task CreateDraftWithGracePeriodOf61MinutesReturnsBadRequest()
    {
        var response = await CreateDraftAsync("ORD-001", """{"minutes":61}""");

        var problem = await AssertValidationProblemAsync(response);
        Assert.IsNotEmpty(problem.Errors["Parameters"]);
    }

    [TestMethod]
    public async Task CreateDraftWithHtmlPromotionLabelReturnsBadRequest()
    {
        var response = await CreateDraftAsync(
            "PRM-001",
            """{"linePromotions":[],"orderPromotions":[{"kind":"PercentOff","value":10,"minSubtotal":0,"label":"<b>Sale</b>"}]}""");

        var problem = await AssertValidationProblemAsync(response);
        Assert.IsNotEmpty(problem.Errors["Parameters"]);
    }

    [TestMethod]
    public async Task PublishShippingFeeIsGuardedWhileV1CallsExist()
    {
        Fixture.OrderingUsage.Calls = 3;
        var draft = await CreateAndReadDraftAsync("SHP-001", """{"fee":5,"freeShippingThreshold":null}""");

        var response = await PublishAsync(draft.Id);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task PublishGracePeriodIsNotGuarded()
    {
        Fixture.OrderingUsage.Calls = 3;
        var draft = await CreateAndReadDraftAsync("ORD-001", """{"minutes":5}""");

        var response = await PublishAsync(draft.Id);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    private async Task<RuleVersionDto> CreateAndReadDraftAsync(string ruleTypeId, string parametersJson)
    {
        var response = await CreateDraftAsync(ruleTypeId, parametersJson);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        var version = await response.Content.ReadFromJsonAsync<RuleVersionDto>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(version);
        return version;
    }

    private async Task<HttpResponseMessage> CreateDraftAsync(string ruleTypeId, string parametersJson)
    {
        var body = new
        {
            ruleTypeId,
            scope = RuleScopes.Default,
            parameters = JsonDocument.Parse(parametersJson).RootElement,
            effectiveFrom = Tomorrow,
            effectiveTo = (DateTimeOffset?)null,
        };

        using var request = NewRequest(HttpMethod.Post, "api/rules/versions", EditorRole, EditorSub);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        return await _httpClient.SendAsync(request, TestContext.CancellationToken);
    }

    private async Task<HttpResponseMessage> PublishAsync(Guid id)
    {
        using var request = NewRequest(HttpMethod.Post, $"api/rules/versions/{id}/publish", PublisherRole, PublisherSub);
        request.Content = JsonContent.Create(new PublishRequest(null), options: JsonOptions);
        return await _httpClient.SendAsync(request, TestContext.CancellationToken);
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string uri, string roles, string sub)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add(TestAuthHandler.ScopesHeader, AdminScope);
        request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        request.Headers.Add(TestAuthHandler.SubjectHeader, sub);
        return request;
    }

    private async Task<HttpValidationProblemDetails> AssertValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(JsonOptions, TestContext.CancellationToken);
        Assert.IsNotNull(problem);
        return problem;
    }
}
