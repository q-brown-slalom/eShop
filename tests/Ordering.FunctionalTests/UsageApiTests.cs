using System.Net;
using System.Net.Http.Json;
using Asp.Versioning;
using Asp.Versioning.Http;
using eShop.Ordering.Infrastructure;
using eShop.Ordering.Infrastructure.Usage;
using Microsoft.EntityFrameworkCore;

namespace eShop.Ordering.FunctionalTests;

public sealed class UsageApiTests : IClassFixture<OrderingApiFixture>
{
    private const string Operation = "ordering.create_order.v1.calls";
    private const string UsageScope = "ordering.usage";

    private readonly OrderingApiFixture _fixture;
    private readonly HttpClient _httpClient;

    public UsageApiTests(OrderingApiFixture fixture)
    {
        var handler = new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(1.0));

        _fixture = fixture;
        _httpClient = fixture.CreateDefaultClient(handler);
    }

    [Fact]
    public async Task UsageReturnsZeroWhenNoV1Calls()
    {
        await ResetUsageAsync();

        var response = await GetUsageAsync(7, UsageScope);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UsageResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(Operation, body.Operation);
        Assert.Equal(7, body.Days);
        Assert.Equal(0, body.Calls);
    }

    [Fact]
    public async Task UsageCountsCallsWithinWindowOnly()
    {
        await ResetUsageAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedUsageAsync(today.AddDays(-8), 5);
        await SeedUsageAsync(today.AddDays(-1), 3);

        var response = await GetUsageAsync(7, UsageScope);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UsageResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(3, body.Calls);
    }

    [Fact]
    public async Task UsageRequiresUsageScope()
    {
        var response = await GetUsageAsync(7, "orders");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RecordUpsertsTodayAndPurgesRowsPastRetention()
    {
        await ResetUsageAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedUsageAsync(today.AddDays(-90), 4);
        await SeedUsageAsync(today.AddDays(-91), 9);

        using (var scope = _fixture.Services.CreateScope())
        {
            var recorder = scope.ServiceProvider.GetRequiredService<IApiUsageRecorder>();
            await recorder.RecordAsync(Operation, TestContext.Current.CancellationToken);
            await recorder.RecordAsync(Operation, TestContext.Current.CancellationToken);
        }

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
            var rows = await context.ApiUsageDaily.AsNoTracking()
                .OrderBy(u => u.Day)
                .Select(u => new { u.Day, u.Calls })
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Collection(rows,
                r => { Assert.Equal(today.AddDays(-90), r.Day); Assert.Equal(4, r.Calls); },
                r => { Assert.Equal(today, r.Day); Assert.Equal(2, r.Calls); });
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public async Task UsageRejectsDaysOutsideOneToThirty(int days)
    {
        var response = await GetUsageAsync(days, UsageScope);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<HttpResponseMessage> GetUsageAsync(int days, string scopes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/usage/create-order-v1?days={days}");
        request.Headers.Add(AutoAuthorizeMiddleware.TestScopesHeader, scopes);
        return await _httpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task ResetUsageAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM ordering.apiusagedaily", TestContext.Current.CancellationToken);
    }

    private async Task SeedUsageAsync(DateOnly day, long calls)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
        await context.Database.ExecuteSqlAsync(
            $"""INSERT INTO ordering.apiusagedaily ("Day", "Operation", "Calls") VALUES ({day}, {Operation}, {calls})""",
            TestContext.Current.CancellationToken);
    }

    private sealed record UsageResponse(string Operation, int Days, long Calls);
}
