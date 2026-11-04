using System.Net;
using System.Text;

namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class HttpRulesSnapshotSourceTests
{
    private static readonly Uri BaseAddress = new("http://businessrules-api/");

    [TestMethod]
    public async Task HttpSourceSendsIfNoneMatchAndMapsNotModified()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        var source = new HttpRulesSnapshotSource(new HttpClient(handler) { BaseAddress = BaseAddress });

        var result = await source.FetchAsync(3, CancellationToken.None);

        Assert.IsTrue(result.NotModified);
        Assert.IsNull(result.Snapshot);
        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Get, request.Method);
        Assert.AreEqual("http://businessrules-api/api/rules/snapshot?api-version=1.0", request.RequestUri!.ToString());
        Assert.AreEqual("\"3\"", request.Headers.IfNoneMatch.Single().Tag);
    }

    [TestMethod]
    public async Task HttpSourceDeserializesSnapshotAndOmitsIfNoneMatchWithoutKnownVersion()
    {
        var published = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new RulesSnapshot(
            5,
            published,
            [new SnapshotRuleVersion(TestRuleType.TypeId, RuleScopes.Default, 2, published, null, JsonDocument.Parse("""{"value":1}""").RootElement.Clone())]);
        var json = JsonSerializer.Serialize(snapshot, RulesSnapshotJson.Options);
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        var source = new HttpRulesSnapshotSource(new HttpClient(handler) { BaseAddress = BaseAddress });

        var result = await source.FetchAsync(null, CancellationToken.None);

        Assert.IsFalse(result.NotModified);
        Assert.AreEqual(5L, result.Snapshot!.Version);
        Assert.AreEqual(2, result.Snapshot.Rules.Single().VersionNumber);
        Assert.IsEmpty(handler.Requests.Single().Headers.IfNoneMatch);
    }

    [TestMethod]
    public async Task HttpSourceThrowsOnServerError()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var source = new HttpRulesSnapshotSource(new HttpClient(handler) { BaseAddress = BaseAddress });

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => source.FetchAsync(3, CancellationToken.None));
    }
}
