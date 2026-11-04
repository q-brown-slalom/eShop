using System.Net.Http.Json;
using System.Text.Json;
using Asp.Versioning;
using Asp.Versioning.Http;
using eShop.BusinessRules.Client.Pricing;
using eShop.Ordering.API.Application.Models;
using eShop.Ordering.API.Application.Queries;
using eShop.Ordering.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace eShop.Ordering.FunctionalTests;

/// <summary>Creates, reads, and removes orders for the auto-authorized test buyer (<see cref="AutoAuthorizeMiddleware.IDENTITY_ID"/>).</summary>
public sealed class TestBuyerOrders
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrderingApiFixture _fixture;
    private readonly HttpClient _httpClientV1;
    private readonly HttpClient _httpClientV2;

    public TestBuyerOrders(OrderingApiFixture fixture)
    {
        _fixture = fixture;
        _httpClientV1 = fixture.CreateDefaultClient(new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(1.0)));
        _httpClientV2 = fixture.CreateDefaultClient(new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(2.0)));
    }

    public async Task<HttpResponseMessage> PostV2Async(Guid requestId, List<QuoteLine> items, decimal expectedTotal, DateTime? cardExpiration = null)
    {
        var body = new CreateOrderV2Request(AutoAuthorizeMiddleware.IDENTITY_ID, "TestUser", "city", "street", "state", "country", "12345",
            "4012888888881881", "Test User", cardExpiration ?? DateTime.UtcNow.AddYears(1), "123", 1, "test buyer", items, expectedTotal);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/orders") { Content = JsonContent.Create(body) };
        request.Headers.Add("x-requestid", requestId.ToString());
        return await _httpClientV2.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public async Task<List<OrderSummary>> GetAllAsync()
    {
        return await _httpClientV1.GetFromJsonAsync<List<OrderSummary>>("api/orders", JsonOptions, TestContext.Current.CancellationToken);
    }

    public async Task<Order> GetSingleAsync()
    {
        var summary = Assert.Single(await GetAllAsync());
        return await _httpClientV1.GetFromJsonAsync<Order>($"api/orders/{summary.OrderNumber}", JsonOptions, TestContext.Current.CancellationToken);
    }

    // Created orders are removed so tests that expect no orders (or seed this buyer) stay independent of run order.
    public async Task DeleteAllAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
        var identity = AutoAuthorizeMiddleware.IDENTITY_ID;
        await context.Database.ExecuteSqlAsync(
            $"""DELETE FROM ordering.orders WHERE "BuyerId" IN (SELECT "Id" FROM ordering.buyers WHERE "IdentityGuid" = {identity})""",
            cancellationToken);
        await context.Database.ExecuteSqlAsync(
            $"""DELETE FROM ordering.paymentmethods WHERE "BuyerId" IN (SELECT "Id" FROM ordering.buyers WHERE "IdentityGuid" = {identity})""",
            cancellationToken);
        await context.Database.ExecuteSqlAsync(
            $"""DELETE FROM ordering.buyers WHERE "IdentityGuid" = {identity}""",
            cancellationToken);
    }
}
