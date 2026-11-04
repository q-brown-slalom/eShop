using System.Net;
using ClientApp.UnitTests.Mocks;
using eShop.ClientApp.Models.Orders;
using eShop.ClientApp.Models.Pricing;
using eShop.ClientApp.Services.Identity;
using eShop.ClientApp.Services.RequestProvider;

namespace ClientApp.UnitTests.Services;

[TestClass]
public class OrdersServiceTests
{
    private readonly ISettingsService _settingsService;

    public OrdersServiceTests()
    {
        _settingsService = new MockSettingsService();
    }
    
    [TestMethod]
    public async Task GetFakeOrderTest()
    {
        var ordersMockService = new OrderMockService();
        var order = await ordersMockService.GetOrderAsync(1);

        Assert.IsNotNull(order);
    }

    [TestMethod]
    public async Task GetFakeOrdersTest()
    {
        var ordersMockService = new OrderMockService();
        var result = await ordersMockService.GetOrdersAsync();

        Assert.AreNotEqual(0, result.Count());
    }

    [TestMethod]
    public async Task OrderServiceUsesApiVersion2ForCreate()
    {
        var (orderService, requestProvider) = await CreateOrderServiceAsync();
        var order = new eShop.ClientApp.Models.Orders.Order
        {
            UserId = "user-1",
            OrderItems =
            [
                new() { ProductId = 7, Quantity = 2, UnitPrice = 1m },
                new() { ProductId = 9, Quantity = 1, UnitPrice = 1m }
            ]
        };

        var result = await orderService.CreateOrderAsync(order, 42.50m);

        Assert.IsInstanceOfType<CreateOrderResult.Created>(result);
        Assert.AreEqual("https://orders/api/orders?api-version=2.0", requestProvider.LastUri);
        Assert.AreEqual("x-requestid", requestProvider.LastHeader);
        var request = (CreateOrderV2Request)requestProvider.LastData!;
        Assert.AreEqual(42.50m, request.ExpectedTotal);
        Assert.AreEqual("user-1", request.Buyer);
        CollectionAssert.AreEqual(new[] { new QuoteLine(7, 2), new QuoteLine(9, 1) }, request.Items.ToArray());
    }

    [TestMethod]
    public async Task OrderServiceUsesApiVersion2ForReads()
    {
        var (orderService, requestProvider) = await CreateOrderServiceAsync();

        await orderService.GetOrdersAsync();

        Assert.AreEqual("https://orders/api/orders?api-version=2.0", requestProvider.LastUri);
    }

    [TestMethod]
    public async Task CreateOrderReturnsPricesChangedWithFreshQuoteOnConflict()
    {
        var (orderService, requestProvider) = await CreateOrderServiceAsync();
        requestProvider.PostException = new HttpRequestExceptionEx(HttpStatusCode.Conflict,
            """
            {
              "type": "https://eshop/problems/prices-changed",
              "title": "Prices changed",
              "status": 409,
              "detail": "The order total changed since it was shown. Review the new quote and retry with its total.",
              "quote": {
                "lines": [
                  { "productId": 7, "productName": "Mug", "pictureUrl": null, "units": 2, "basePrice": 10.00, "unitPrice": 9.00, "discount": 0.50, "promotionLabel": "Sale", "lineTotal": 17.50 }
                ],
                "subtotal": 18.00,
                "discountTotal": 0.50,
                "shippingFee": 0,
                "total": 17.50,
                "snapshotVersion": 3
              }
            }
            """);

        var result = await orderService.CreateOrderAsync(
            new eShop.ClientApp.Models.Orders.Order { OrderItems = [new() { ProductId = 7, Quantity = 2 }] }, 20m);

        var changed = result as CreateOrderResult.PricesChanged;
        Assert.IsNotNull(changed);
        Assert.AreEqual(17.50m, changed.Quote.Total);
        Assert.AreEqual(3, changed.Quote.SnapshotVersion);
        Assert.AreEqual(9.00m, changed.Quote.Lines[0].UnitPrice);
        Assert.AreEqual("Sale", changed.Quote.Lines[0].PromotionLabel);
    }

    [TestMethod]
    public async Task CreateOrderRethrowsConflictThatIsNotAPriceChange()
    {
        var (orderService, requestProvider) = await CreateOrderServiceAsync();
        requestProvider.PostException = new HttpRequestExceptionEx(HttpStatusCode.Conflict,
            """{ "type": "https://example/other", "status": 409 }""");

        await Assert.ThrowsExactlyAsync<HttpRequestExceptionEx>(() => orderService.CreateOrderAsync(
            new eShop.ClientApp.Models.Orders.Order { OrderItems = [new() { ProductId = 7, Quantity = 2 }] }, 20m));
    }

    [TestMethod]
    public async Task GetQuotePostsLinesToPricingQuoteV1()
    {
        var (orderService, requestProvider) = await CreateOrderServiceAsync();
        var quote = MockOrderService.QuoteOf(12.34m);
        requestProvider.Response = quote;

        var result = await orderService.GetQuoteAsync([new QuoteLine(7, 2)]);

        Assert.AreSame(quote, result);
        Assert.AreEqual("https://orders/api/pricing/quote?api-version=1.0", requestProvider.LastUri);
        var request = (PriceQuoteRequest)requestProvider.LastData!;
        CollectionAssert.AreEqual(new[] { new QuoteLine(7, 2) }, request.Items.ToArray());
    }

    private async Task<(OrderService, MockRequestProvider)> CreateOrderServiceAsync()
    {
        var identityService = new IdentityMockService();
        await identityService.SignInAsync();
        _settingsService.GatewayOrdersEndpointBase = "https://orders";
        var requestProvider = new MockRequestProvider();

        return (new OrderService(identityService, _settingsService, requestProvider), requestProvider);
    }
}
