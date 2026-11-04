using System.Net;
using System.Text;
using eShop.BusinessRules.API.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class V1ClientsPublishGuardTests
{
    private readonly IOrderingUsageClient _usageClient = Substitute.For<IOrderingUsageClient>();

    public TestContext TestContext { get; set; } = null!;

    private V1ClientsPublishGuard CreateGuard() => new(_usageClient, NullLogger<V1ClientsPublishGuard>.Instance);

    [TestMethod]
    public async Task NonPriceRulePublishesWithoutUsageCheck()
    {
        var result = await CreateGuard().CheckAsync(new TestRuleType(), null, TestContext.CancellationToken);

        Assert.IsTrue(result.Allowed);
        Assert.IsFalse(result.Overridden);
        await _usageClient.DidNotReceiveWithAnyArgs().GetCreateOrderV1CallsAsync(default, default);
    }

    [TestMethod]
    public async Task PriceRulePublishesWhenNoV1Calls()
    {
        _usageClient.GetCreateOrderV1CallsAsync(7, Arg.Any<CancellationToken>()).Returns(0L);

        var result = await CreateGuard().CheckAsync(new TestPriceRuleType(), null, TestContext.CancellationToken);

        Assert.IsTrue(result.Allowed);
        Assert.IsFalse(result.Overridden);
    }

    [TestMethod]
    public async Task PriceRuleBlockedWhileV1CallsInLast7Days()
    {
        _usageClient.GetCreateOrderV1CallsAsync(7, Arg.Any<CancellationToken>()).Returns(42L);

        var result = await CreateGuard().CheckAsync(new TestPriceRuleType(), null, TestContext.CancellationToken);

        Assert.IsFalse(result.Allowed);
        Assert.AreEqual("https://eshop/problems/v1-clients-active", result.ProblemType);
        Assert.IsNotNull(result.Detail);
        Assert.Contains("42", result.Detail);
    }

    [TestMethod]
    public async Task PriceRuleBlockedWhenOverrideReasonIsBlank()
    {
        _usageClient.GetCreateOrderV1CallsAsync(7, Arg.Any<CancellationToken>()).Returns(3L);

        var result = await CreateGuard().CheckAsync(new TestPriceRuleType(), "   ", TestContext.CancellationToken);

        Assert.IsFalse(result.Allowed);
        Assert.AreEqual(PublishProblemTypes.V1ClientsActive, result.ProblemType);
    }

    [TestMethod]
    public async Task PriceRulePublishesWithOverrideReasonAndAudits()
    {
        _usageClient.GetCreateOrderV1CallsAsync(7, Arg.Any<CancellationToken>()).Returns(5L);

        var result = await CreateGuard().CheckAsync(new TestPriceRuleType(), "Legacy app retired today", TestContext.CancellationToken);

        Assert.IsTrue(result.Allowed);
        // Overridden tells the publishing service to write the GuardOverridden audit entry with the reason.
        Assert.IsTrue(result.Overridden);
        Assert.IsNull(result.ProblemType);
    }

    [TestMethod]
    public async Task PriceRuleBlockedWhenUsageUnavailable()
    {
        _usageClient.GetCreateOrderV1CallsAsync(7, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("ordering-api unreachable"));

        var result = await CreateGuard().CheckAsync(new TestPriceRuleType(), "Legacy app retired today", TestContext.CancellationToken);

        Assert.IsFalse(result.Allowed);
        Assert.AreEqual(PublishProblemTypes.GuardUnavailable, result.ProblemType);
    }

    [TestMethod]
    public async Task OverrideReasonLongerThan500CharsIsRejected()
    {
        _usageClient.GetCreateOrderV1CallsAsync(7, Arg.Any<CancellationToken>()).Returns(5L);

        var result = await CreateGuard().CheckAsync(new TestPriceRuleType(), new string('x', 501), TestContext.CancellationToken);

        Assert.IsFalse(result.Allowed);
        Assert.IsFalse(result.Overridden);
        Assert.AreEqual(PublishProblemTypes.InvalidOverrideReason, result.ProblemType);
    }

    [TestMethod]
    public async Task OverrideReasonOf500CharsIsAccepted()
    {
        _usageClient.GetCreateOrderV1CallsAsync(7, Arg.Any<CancellationToken>()).Returns(5L);

        var result = await CreateGuard().CheckAsync(new TestPriceRuleType(), new string('x', 500), TestContext.CancellationToken);

        Assert.IsTrue(result.Allowed);
        Assert.IsTrue(result.Overridden);
    }

    [TestMethod]
    public async Task UsageClientRequestsCreateOrderV1UsageAndReadsCalls()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"operation":"create-order-v1","days":7,"calls":12}""", Encoding.UTF8, "application/json"),
        });
        var client = new OrderingUsageClient(new HttpClient(handler) { BaseAddress = new Uri("http://ordering-api/") });

        var calls = await client.GetCreateOrderV1CallsAsync(7, TestContext.CancellationToken);

        Assert.AreEqual(12L, calls);
        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Get, request.Method);
        Assert.AreEqual("http://ordering-api/api/usage/create-order-v1?days=7", request.RequestUri!.ToString());
    }

    [TestMethod]
    public async Task UsageClientThrowsOnErrorStatus()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = new OrderingUsageClient(new HttpClient(handler) { BaseAddress = new Uri("http://ordering-api/") });

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.GetCreateOrderV1CallsAsync(7, TestContext.CancellationToken));
    }
}
