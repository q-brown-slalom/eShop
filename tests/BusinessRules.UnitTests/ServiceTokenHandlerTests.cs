using System.Net;
using System.Text;
using eShop.BusinessRules.Client.Auth;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class ServiceTokenHandlerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
    private readonly StubHttpMessageHandler _identity;
    private readonly StubHttpMessageHandler _api = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
    private readonly HttpMessageInvoker _invoker;
    private int _tokenCount;

    public ServiceTokenHandlerTests()
    {
        _timeProvider.GetUtcNow().Returns(Start);
        _identity = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"access_token":"token-{{++_tokenCount}}","expires_in":3600,"token_type":"Bearer"}""",
                Encoding.UTF8,
                "application/json"),
        });

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_identity, disposeHandler: false));
        var options = Options.Create(new ServiceClientOptions
        {
            ClientId = "businessrules-reader",
            ClientSecret = "secret",
            IdentityUrl = "http://identity-api",
        });
        var provider = new ServiceTokenProvider(factory, options, "businessrules", _timeProvider);
        _invoker = new HttpMessageInvoker(new ServiceTokenHandler(provider) { InnerHandler = _api });
    }

    private Task<HttpResponseMessage> SendAsync()
        => _invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://businessrules-api/api/rules/snapshot"), CancellationToken.None);

    [TestMethod]
    public async Task ServiceTokenHandlerReusesTokenUntilNearExpiry()
    {
        await SendAsync();
        await SendAsync();

        Assert.HasCount(1, _identity.Requests);
        Assert.AreEqual("Bearer token-1", _api.Requests[1].Headers.Authorization!.ToString());

        _timeProvider.GetUtcNow().Returns(Start.AddSeconds(3600 - 60));
        await SendAsync();

        Assert.HasCount(2, _identity.Requests);
        Assert.AreEqual("Bearer token-2", _api.Requests[2].Headers.Authorization!.ToString());
    }

    [TestMethod]
    public async Task ServiceTokenHandlerPostsClientCredentialsToIdentityTokenEndpoint()
    {
        await SendAsync();

        var request = _identity.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, request.Method);
        Assert.AreEqual("http://identity-api/connect/token", request.RequestUri!.ToString());
        var body = _identity.RequestBodies.Single()!;
        Assert.Contains("grant_type=client_credentials", body);
        Assert.Contains("scope=businessrules", body);
        Assert.Contains("client_id=businessrules-reader", body);
        Assert.Contains("client_secret=secret", body);
    }
}
