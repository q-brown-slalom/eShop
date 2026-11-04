namespace eShop.Ordering.UnitTests.Application;

using System.Net;
using System.Net.Http;
using System.Text;
using eShop.Ordering.API.Application.Pricing;

[TestClass]
public class CatalogPriceClientTests
{
    [TestMethod]
    public async Task GetPricesRequestsItemsByIdsWithApiVersionAndMapsPrices()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            [
              { "id": 1, "name": "Alpine Fleece", "description": "warm", "price": 10.50, "pictureFileName": "1.webp", "catalogTypeId": 3, "catalogBrandId": 7, "availableStock": 4 },
              { "id": 2, "name": "Trail Boots", "price": 99.99, "catalogTypeId": 4, "catalogBrandId": 8 }
            ]
            """);
        var client = CreateClient(handler);

        var prices = await client.GetPricesAsync([1, 2, 3], CancellationToken.None);

        Assert.AreEqual("/api/catalog/items/by?ids=1&ids=2&ids=3&api-version=1.0", handler.Request.RequestUri.PathAndQuery);
        Assert.HasCount(2, prices);
        var price = prices[1];
        Assert.AreEqual(1, price.ProductId);
        Assert.AreEqual("Alpine Fleece", price.ProductName);
        Assert.AreEqual(10.50m, price.BasePrice);
        Assert.AreEqual(3, price.CatalogTypeId);
        Assert.AreEqual(7, price.CatalogBrandId);
        Assert.AreEqual(99.99m, prices[2].BasePrice);
        Assert.IsFalse(prices.ContainsKey(3));
    }

    [TestMethod]
    public async Task GetPricesWithNoIdsDoesNotCallCatalog()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        var client = CreateClient(handler);

        var prices = await client.GetPricesAsync([], CancellationToken.None);

        Assert.IsEmpty(prices);
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    public async Task GetPricesWrapsErrorResponseAsCatalogUnavailable()
    {
        var client = CreateClient(new StubHandler(HttpStatusCode.InternalServerError, "boom"));

        await Assert.ThrowsExactlyAsync<CatalogUnavailableException>(() => client.GetPricesAsync([1], CancellationToken.None));
    }

    [TestMethod]
    public async Task GetPricesWrapsTransportFailureAsCatalogUnavailable()
    {
        var client = CreateClient(new StubHandler(new HttpRequestException("connection refused")));

        await Assert.ThrowsExactlyAsync<CatalogUnavailableException>(() => client.GetPricesAsync([1], CancellationToken.None));
    }

    [TestMethod]
    public async Task GetPricesPropagatesCallerCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = CreateClient(new StubHandler(HttpStatusCode.OK, "[]"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetPricesAsync([1], cts.Token));
    }

    private static CatalogPriceClient CreateClient(StubHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("http://catalog-api") });

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly Exception _failure;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public StubHandler(Exception failure) => _failure = failure;

        public HttpRequestMessage Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            if (_failure is not null)
            {
                throw _failure;
            }

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
