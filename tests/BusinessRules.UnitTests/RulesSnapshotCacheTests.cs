using eShop.BusinessRules.Client.IntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class RulesSnapshotCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly IRulesSnapshotSource _source = Substitute.For<IRulesSnapshotSource>();
    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
    private readonly RulesSnapshotCache _cache;

    public RulesSnapshotCacheTests()
    {
        _timeProvider.GetUtcNow().Returns(Now);
        _cache = new RulesSnapshotCache(_source, _timeProvider, NullLogger<RulesSnapshotCache>.Instance);
    }

    private static RulesSnapshot Snapshot(long version) => new(version, Now.AddMinutes(-1), []);

    private void SourceReturns(long version)
        => _source.FetchAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(new SnapshotFetchResult(false, Snapshot(version)));

    [TestMethod]
    public void CacheStartsWithEmptySnapshot()
    {
        Assert.AreEqual(0L, _cache.Current.Version);
        Assert.IsNull(_cache.LastRefreshedAt);
    }

    [TestMethod]
    public async Task RefreshReplacesSnapshotWhenSourceReturnsNewerVersion()
    {
        SourceReturns(3);

        var refreshed = await _cache.RefreshAsync(CancellationToken.None);

        Assert.IsTrue(refreshed);
        Assert.AreEqual(3L, _cache.Current.Version);
        Assert.AreEqual(Now, _cache.LastRefreshedAt);
    }

    [TestMethod]
    public async Task RefreshKeepsLastSnapshotWhenSourceThrows()
    {
        SourceReturns(3);
        await _cache.RefreshAsync(CancellationToken.None);
        _source.FetchAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("unavailable"));

        var refreshed = await _cache.RefreshAsync(CancellationToken.None);

        Assert.IsFalse(refreshed);
        Assert.AreEqual(3L, _cache.Current.Version);
    }

    [TestMethod]
    public async Task RefreshIgnoresOlderVersion()
    {
        SourceReturns(3);
        await _cache.RefreshAsync(CancellationToken.None);
        SourceReturns(2);

        await _cache.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(3L, _cache.Current.Version);
    }

    [TestMethod]
    public async Task RefreshPassesKnownVersionAndKeepsSnapshotWhenNotModified()
    {
        SourceReturns(3);
        await _cache.RefreshAsync(CancellationToken.None);
        _source.FetchAsync(3, Arg.Any<CancellationToken>()).Returns(new SnapshotFetchResult(true, null));

        var refreshed = await _cache.RefreshAsync(CancellationToken.None);

        Assert.IsTrue(refreshed);
        Assert.AreEqual(3L, _cache.Current.Version);
        await _source.Received(1).FetchAsync(3, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RefreshSendsNoKnownVersionWhenCacheIsEmpty()
    {
        SourceReturns(1);

        await _cache.RefreshAsync(CancellationToken.None);

        await _source.Received(1).FetchAsync(null, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task HandlerSkipsRefreshWhenEventVersionNotNewer()
    {
        SourceReturns(3);
        await _cache.RefreshAsync(CancellationToken.None);
        _source.ClearReceivedCalls();
        var handler = new BusinessRulesSnapshotPublishedIntegrationEventHandler(
            _cache, NullLogger<BusinessRulesSnapshotPublishedIntegrationEventHandler>.Instance);

        await handler.Handle(new BusinessRulesSnapshotPublishedIntegrationEvent(3, Now));

        await _source.DidNotReceive().FetchAsync(Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task HandlerRefreshesWhenEventVersionIsNewer()
    {
        SourceReturns(3);
        await _cache.RefreshAsync(CancellationToken.None);
        SourceReturns(4);
        var handler = new BusinessRulesSnapshotPublishedIntegrationEventHandler(
            _cache, NullLogger<BusinessRulesSnapshotPublishedIntegrationEventHandler>.Instance);

        await handler.Handle(new BusinessRulesSnapshotPublishedIntegrationEvent(4, Now));

        Assert.AreEqual(4L, _cache.Current.Version);
    }
}
