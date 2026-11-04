using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using Asp.Versioning.Http;
using eShop.BusinessRules.Client.Pricing;
using eShop.Ordering.API.Application.Commands;
using eShop.Ordering.API.Application.Models;
using eShop.Ordering.API.Application.Queries;
using eShop.Ordering.Domain.AggregatesModel.BuyerAggregate;
using eShop.Ordering.Infrastructure;
using eShop.Ordering.Infrastructure.Idempotency;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Address = eShop.Ordering.Domain.AggregatesModel.OrderAggregate.Address;
using DomainOrder = eShop.Ordering.Domain.AggregatesModel.OrderAggregate.Order;

namespace eShop.Ordering.FunctionalTests;

public sealed class OrderingApiTests : IClassFixture<OrderingApiFixture>
{
    private readonly WebApplicationFactory<Program> _webApplicationFactory;
    private readonly HttpClient _httpClient;
    private readonly HttpClient _httpClientV2;

    public OrderingApiTests(OrderingApiFixture fixture)
    {
        var handler = new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(1.0));

        _webApplicationFactory = fixture;
        _httpClient = _webApplicationFactory.CreateDefaultClient(handler);
        _httpClientV2 = _webApplicationFactory.CreateDefaultClient(new ApiVersionHandler(new QueryStringApiVersionWriter(), new ApiVersion(2.0)));
    }

    [Fact]
    public async Task GetAllStoredOrdersWorks()
    {
        // Act
        var response = await _httpClient.GetAsync("api/orders", TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CancelWithEmptyGuidFails()
    {
        // Act
        var content = new StringContent(BuildOrder(), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.Empty.ToString() } }
        };
        var response = await _httpClient.PutAsync("/api/orders/cancel", content, TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CancelNonExistentOrderFails()
    {
        // Act
        var content = new StringContent(BuildOrder(), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.NewGuid().ToString() } }
        };
        var response = await _httpClient.PutAsync("api/orders/cancel", content, TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task ShipWithEmptyGuidFails()
    {
        // Act
        var content = new StringContent(BuildOrder(), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.Empty.ToString() } }
        };
        var response = await _httpClient.PutAsync("api/orders/ship", content, TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ShipNonExistentOrderFails()
    {
        // Act
        var content = new StringContent(BuildOrder(), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.NewGuid().ToString() } }
        };
        var response = await _httpClient.PutAsync("api/orders/ship", content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetAllOrdersCardType()
    {
        // Act 1
        var response = await _httpClient.GetAsync("api/orders/cardtypes", TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetStoredOrdersWithOrderId()
    {
        // Act
        var response = await _httpClient.GetAsync("api/orders/1", TestContext.Current.CancellationToken);
        var responseStatus = response.StatusCode;

        // Assert
        Assert.Equal("NotFound", responseStatus.ToString());
    }

    [Fact]
    public async Task AddNewEmptyOrder()
    {
        // Act
        var content = new StringContent(JsonSerializer.Serialize(new Order()), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.Empty.ToString() } }
        };
        var response = await _httpClient.PostAsync("api/orders", content, TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddNewOrder()
    {
        try
        {
            // Act
            var item = new BasketItem
            {
                Id = "1",
                ProductId = 1,
                ProductName = "Test",
                UnitPrice = 10,
                OldUnitPrice = 9,
                Quantity = 1,
                PictureUrl = null
            };
            var cardExpirationDate = Convert.ToDateTime("2023-12-22T12:34:24.334Z");
            var OrderRequest = new CreateOrderRequest("1", "TestUser", null, null, null, null, null, "XXXXXXXXXXXX0005", "Test User", cardExpirationDate, "test buyer", 1, null, new List<BasketItem> { item });
            var content = new StringContent(JsonSerializer.Serialize(OrderRequest), UTF8Encoding.UTF8, "application/json")
            {
                Headers = { { "x-requestid", Guid.NewGuid().ToString() } }
            };
            var response = await _httpClient.PostAsync("api/orders", content, TestContext.Current.CancellationToken);
            var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await DeleteTestBuyerOrdersAsync();
        }
    }

    [Fact]
    public async Task CreateOrderV2WithMatchingExpectedTotalCreatesOrderAtServerPrices()
    {
        try
        {
            var response = await PostOrderV2Async(Guid.NewGuid(), [new QuoteLine(1, 2), new QuoteLine(3, 1)], expectedTotal: 25.25m);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var order = await GetSingleTestBuyerOrderAsync();
            Assert.Equal(25.25m, order.Total);
            Assert.Equal(25.25m, order.Subtotal);
            Assert.Equal(0m, order.ShippingFee);
            Assert.Collection(order.OrderItems.OrderBy(i => i.UnitPrice),
                i => { Assert.Equal(5.25, i.UnitPrice); Assert.Equal(1, i.Units); Assert.Equal("Fake Product 3", i.ProductName); },
                i => { Assert.Equal(10.00, i.UnitPrice); Assert.Equal(2, i.Units); Assert.Equal("Fake Product 1", i.ProductName); });
        }
        finally
        {
            await DeleteTestBuyerOrdersAsync();
        }
    }

    [Fact]
    public async Task CreateOrderV2WithStaleExpectedTotalReturnsConflictWithFreshQuote()
    {
        try
        {
            var requestId = Guid.NewGuid();

            var response = await PostOrderV2Async(requestId, [new QuoteLine(1, 2), new QuoteLine(3, 1)], expectedTotal: 20.00m);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("https://eshop/problems/prices-changed", problem.RootElement.GetProperty("type").GetString());
            var quote = problem.RootElement.GetProperty("quote");
            Assert.Equal(25.25m, quote.GetProperty("total").GetDecimal());
            Assert.Equal(2, quote.GetProperty("lines").GetArrayLength());
            Assert.Empty(await GetTestBuyerOrdersAsync());
            Assert.False(await ClientRequestExistsAsync(requestId));
        }
        finally
        {
            await DeleteTestBuyerOrdersAsync();
        }
    }

    [Fact]
    public async Task CreateOrderV2WithUnknownProductReturnsBadRequest()
    {
        try
        {
            var response = await PostOrderV2Async(Guid.NewGuid(), [new QuoteLine(1, 1), new QuoteLine(777, 1)], expectedTotal: 10.00m);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(TestContext.Current.CancellationToken);
            var message = Assert.Single(Assert.Contains("items", problem.Errors));
            Assert.Contains("777", message);
            Assert.Empty(await GetTestBuyerOrdersAsync());
        }
        finally
        {
            await DeleteTestBuyerOrdersAsync();
        }
    }

    [Fact]
    public async Task RetryAfterPriceConflictWithSameRequestIdCreatesOrder()
    {
        try
        {
            var requestId = Guid.NewGuid();
            List<QuoteLine> items = [new QuoteLine(2, 1)];

            var conflict = await PostOrderV2Async(requestId, items, expectedTotal: 19.99m);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

            var retry = await PostOrderV2Async(requestId, items, expectedTotal: 20.50m);

            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var order = await GetSingleTestBuyerOrderAsync();
            Assert.Equal(20.50m, order.Total);
            Assert.True(await ClientRequestExistsAsync(requestId));
        }
        finally
        {
            await DeleteTestBuyerOrdersAsync();
        }
    }

    [Fact]
    public async Task RetryAfterFailedCreateWithSameRequestIdCreatesOrder()
    {
        try
        {
            var requestId = Guid.NewGuid();
            List<QuoteLine> items = [new QuoteLine(2, 1)];

            var failed = await PostOrderV2Async(requestId, items, expectedTotal: 20.50m, cardExpiration: DateTime.UtcNow.AddYears(-1));
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.Empty(await GetTestBuyerOrdersAsync());
            Assert.False(await ClientRequestExistsAsync(requestId));

            var retry = await PostOrderV2Async(requestId, items, expectedTotal: 20.50m);

            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var order = await GetSingleTestBuyerOrderAsync();
            Assert.Equal(20.50m, order.Total);
            Assert.True(await ClientRequestExistsAsync(requestId));
        }
        finally
        {
            await DeleteTestBuyerOrdersAsync();
        }
    }

    [Fact]
    public async Task CreateOrderV1IgnoresClientUnitPrice()
    {
        try
        {
            var item = new BasketItem { Id = "1", ProductId = 2, ProductName = "Tampered", UnitPrice = 0.01m, OldUnitPrice = 0.01m, Quantity = 2 };

            var response = await PostOrderV1Async(Guid.NewGuid(), item);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var order = await GetSingleTestBuyerOrderAsync();
            Assert.Equal(41.00m, order.Total);
            var line = Assert.Single(order.OrderItems);
            Assert.Equal(20.50, line.UnitPrice);
            Assert.Equal("Fake Product 2", line.ProductName);
        }
        finally
        {
            await DeleteTestBuyerOrdersAsync();
        }
    }

    [Fact]
    public async Task CreateOrderV1RecordsUsage()
    {
        try
        {
            using (var scope = _webApplicationFactory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
                await context.Database.ExecuteSqlRawAsync("DELETE FROM ordering.apiusagedaily", TestContext.Current.CancellationToken);
            }

            var response = await PostOrderV1Async(Guid.NewGuid(), new BasketItem { Id = "1", ProductId = 5, ProductName = "x", UnitPrice = 0.99m, Quantity = 1 });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var request = new HttpRequestMessage(HttpMethod.Get, "api/usage/create-order-v1?days=7");
            request.Headers.Add(AutoAuthorizeMiddleware.TestScopesHeader, "ordering.usage");
            var usage = await _httpClient.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, usage.StatusCode);
            using var body = JsonDocument.Parse(await usage.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(1, body.RootElement.GetProperty("calls").GetInt64());
        }
        finally
        {
            await DeleteTestBuyerOrdersAsync();
        }
    }

    [Fact]
    public async Task PostDraftOrder()
    {
        // Act
        var item = new BasketItem
        {
            Id = "1",
            ProductId = 12,
            ProductName = "Test",
            UnitPrice = 10,
            OldUnitPrice = 9,
            Quantity = 1,
            PictureUrl = null
        };
        var bodyContent = new CustomerBasket("1", new List<BasketItem> { item });
        var content = new StringContent(JsonSerializer.Serialize(bodyContent), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.NewGuid().ToString() } }
        };
        var response = await _httpClient.PostAsync("api/orders/draft", content, TestContext.Current.CancellationToken);
        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetOrderReturnsPricingBreakdown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        int orderId;
        int buyerId;

        // Seeded through the context so the order carries a discount and shipping fee that no published rule produces yet.
        using (var scope = _webApplicationFactory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
            var buyer = context.Buyers.Add(new Buyer(AutoAuthorizeMiddleware.IDENTITY_ID, "Pricing Buyer")).Entity;
            var order = new DomainOrder("userId", "Pricing Buyer", new Address("street", "city", "state", "country", "zipcode"),
                cardTypeId: 1, cardNumber: "XXXXXXXXXXXX0005", cardSecurityNumber: "123", cardHolderName: "name",
                cardExpiration: DateTime.UtcNow.AddYears(1), buyerId: buyer.Id);
            order.AddOrderItem(1, "cup", 10m, 3m, string.Empty, units: 2);
            order.AddOrderItem(2, "mug", 5m, 0m, string.Empty);
            order.ApplyPricing(shippingFee: 4.99m, pricingSnapshotVersion: 7);
            context.Orders.Add(order);
            await context.SaveChangesAsync(cancellationToken);
            orderId = order.Id;
            buyerId = buyer.Id;
        }

        try
        {
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

            var order = await _httpClient.GetFromJsonAsync<Order>($"api/orders/{orderId}", jsonOptions, cancellationToken);

            Assert.Equal(25m, order.Subtotal);
            Assert.Equal(3m, order.DiscountTotal);
            Assert.Equal(4.99m, order.ShippingFee);
            Assert.Equal(26.99m, order.Total);
            Assert.Equal(3m, order.OrderItems.Sum(i => i.Discount));

            var summaries = await _httpClient.GetFromJsonAsync<List<OrderSummary>>("api/orders", jsonOptions, cancellationToken);
            var summary = Assert.Single(summaries, s => s.OrderNumber == orderId);
            Assert.Equal(26.99, summary.Total);
        }
        finally
        {
            // Remove the seeded order so tests that expect an empty orders table stay independent of run order.
            using var scope = _webApplicationFactory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
            await context.Orders.Where(o => o.Id == orderId).ExecuteDeleteAsync(cancellationToken);
            await context.Buyers.Where(b => b.Id == buyerId).ExecuteDeleteAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task CreateOrderDraftSucceeds()
    {
        var payload = FakeOrderDraftCommand();
        var content = new StringContent(JsonSerializer.Serialize(FakeOrderDraftCommand()), UTF8Encoding.UTF8, "application/json")
        {
            Headers = { { "x-requestid", Guid.NewGuid().ToString() } }
        };
        var response = await _httpClient.PostAsync("api/orders/draft", content, TestContext.Current.CancellationToken);

        var s = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var responseData = JsonSerializer.Deserialize<OrderDraftDTO>(s, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload.Items.Count(), responseData.OrderItems.Count());
        Assert.Equal(payload.Items.Sum(o => o.Quantity * o.UnitPrice), responseData.Total);
        AssertThatOrderItemsAreTheSameAsRequestPayloadItems(payload, responseData);
    }

    private CreateOrderDraftCommand FakeOrderDraftCommand()
    {
        return new CreateOrderDraftCommand(
            BuyerId: Guid.NewGuid().ToString(),
            new List<BasketItem>()
            {
                new BasketItem()
                {
                    Id = Guid.NewGuid().ToString(),
                    ProductId = 1,
                    ProductName = "Test Product 1",
                    UnitPrice = 10.2m,
                    OldUnitPrice = 9.8m,
                    Quantity = 2,
                    PictureUrl = Guid.NewGuid().ToString(),
                }
            });
    }

    private static void AssertThatOrderItemsAreTheSameAsRequestPayloadItems(CreateOrderDraftCommand payload, OrderDraftDTO responseData)
    {
        // check that OrderItems contain all product Ids from the payload
        var payloadItemsProductIds = payload.Items.Select(x => x.ProductId);
        var orderItemsProductIds = responseData.OrderItems.Select(x => x.ProductId);
        Assert.All(orderItemsProductIds, orderItemProdId => payloadItemsProductIds.Contains(orderItemProdId));
        // TODO: might need to add more asserts in here
    }

    string BuildOrder()
    {
        var order = new
        {
            OrderNumber = "-1"
        };
        return JsonSerializer.Serialize(order);
    }

    private async Task<HttpResponseMessage> PostOrderV2Async(Guid requestId, List<QuoteLine> items, decimal expectedTotal, DateTime? cardExpiration = null)
    {
        var body = new CreateOrderV2Request(AutoAuthorizeMiddleware.IDENTITY_ID, "TestUser", "city", "street", "state", "country", "12345",
            "4012888888881881", "Test User", cardExpiration ?? DateTime.UtcNow.AddYears(1), "123", 1, "test buyer", items, expectedTotal);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/orders") { Content = JsonContent.Create(body) };
        request.Headers.Add("x-requestid", requestId.ToString());
        return await _httpClientV2.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> PostOrderV1Async(Guid requestId, params BasketItem[] items)
    {
        var body = new CreateOrderRequest(AutoAuthorizeMiddleware.IDENTITY_ID, "TestUser", "city", "street", "state", "country", "12345",
            "4012888888881881", "Test User", DateTime.UtcNow.AddYears(1), "123", 1, "test buyer", items.ToList());
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/orders") { Content = JsonContent.Create(body) };
        request.Headers.Add("x-requestid", requestId.ToString());
        return await _httpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<List<OrderSummary>> GetTestBuyerOrdersAsync()
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return await _httpClient.GetFromJsonAsync<List<OrderSummary>>("api/orders", jsonOptions, TestContext.Current.CancellationToken);
    }

    private async Task<Order> GetSingleTestBuyerOrderAsync()
    {
        var summary = Assert.Single(await GetTestBuyerOrdersAsync());
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return await _httpClient.GetFromJsonAsync<Order>($"api/orders/{summary.OrderNumber}", jsonOptions, TestContext.Current.CancellationToken);
    }

    private async Task<bool> ClientRequestExistsAsync(Guid requestId)
    {
        using var scope = _webApplicationFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
        return await context.Set<ClientRequest>().AnyAsync(r => r.Id == requestId, TestContext.Current.CancellationToken);
    }

    // Created orders are removed so tests that expect no orders (or seed this buyer) stay independent of run order.
    private async Task DeleteTestBuyerOrdersAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = _webApplicationFactory.Services.CreateScope();
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
