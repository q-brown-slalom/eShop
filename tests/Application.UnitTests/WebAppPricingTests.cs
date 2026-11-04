using System.Net;
using System.Text;
using System.Text.Json;
using eShop.BusinessRules.Client.Pricing;
using eShop.WebApp.Services;
using eShop.WebAppComponents.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace eShop.Application.UnitTests;

[TestClass]
public class WebAppPricingTests
{
    private const string PricesChangedType = "https://eshop/problems/prices-changed";

    [TestMethod]
    public async Task OrderingServiceThrowsPricesChangedWithFreshQuoteOn409()
    {
        var freshQuote = Quote(new PricedLine(7, "Watch", null, 2, 50m, 45m, 5m, "Sale"), shippingFee: 4.99m);
        var handler = new StubHandler(_ => Json(HttpStatusCode.Conflict, new Dictionary<string, object>
        {
            ["type"] = PricesChangedType,
            ["title"] = "Prices changed",
            ["status"] = 409,
            ["detail"] = "The order total changed since it was shown.",
            ["quote"] = freshQuote
        }, "application/problem+json"));
        using var services = BuildServices(handler);

        var ordering = services.GetRequiredService<OrderingService>();
        var ex = await Assert.ThrowsExactlyAsync<PricesChangedException>(
            () => ordering.CreateOrder(OrderRequest(expectedTotal: 90m), Guid.NewGuid()));

        Assert.AreEqual(freshQuote.Total, ex.Quote.Total);
        Assert.AreEqual(freshQuote.Subtotal, ex.Quote.Subtotal);
        Assert.AreEqual(freshQuote.DiscountTotal, ex.Quote.DiscountTotal);
        Assert.AreEqual(freshQuote.ShippingFee, ex.Quote.ShippingFee);
        Assert.AreEqual(freshQuote.SnapshotVersion, ex.Quote.SnapshotVersion);
        Assert.HasCount(1, ex.Quote.Lines);
        Assert.AreEqual(freshQuote.Lines[0], ex.Quote.Lines[0]);
    }

    [TestMethod]
    public async Task OrderingServiceThrowsHttpRequestExceptionOnOtherConflicts()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Conflict, new { type = "https://example/other", title = "Other" }, "application/problem+json"));
        using var services = BuildServices(handler);

        var ordering = services.GetRequiredService<OrderingService>();

        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => ordering.CreateOrder(OrderRequest(expectedTotal: 90m), Guid.NewGuid()));
    }

    [TestMethod]
    public async Task OrderingServiceSendsApiVersion2AndRequestId()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var services = BuildServices(handler);
        var requestId = Guid.NewGuid();

        await services.GetRequiredService<OrderingService>().CreateOrder(OrderRequest(expectedTotal: 90.5m), requestId);

        var request = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, request.Method);
        Assert.AreEqual("/api/orders/", request.Uri.AbsolutePath, ignoreCase: true);
        Assert.AreEqual("?api-version=2.0", request.Uri.Query);
        Assert.AreEqual(requestId.ToString(), request.Headers["x-requestid"]);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.AreEqual(90.5m, body.RootElement.GetProperty("expectedTotal").GetDecimal());
        var item = body.RootElement.GetProperty("items")[0];
        Assert.AreEqual(7, item.GetProperty("productId").GetInt32());
        Assert.AreEqual(2, item.GetProperty("units").GetInt32());
    }

    [TestMethod]
    public async Task OrderingServiceSendsCreateOrderV1BodyWhenConfigured()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var services = BuildServices(handler, new() { ["Ordering:UseCreateOrderV1"] = "true" });

        await services.GetRequiredService<OrderingService>().CreateOrder(OrderRequest(expectedTotal: 90.5m), Guid.NewGuid());

        var request = handler.Requests.Single();
        Assert.AreEqual("?api-version=1.0", request.Uri.Query);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.IsFalse(body.RootElement.TryGetProperty("expectedTotal", out _));
        var item = body.RootElement.GetProperty("items")[0];
        Assert.AreEqual(7, item.GetProperty("productId").GetInt32());
        Assert.AreEqual(2, item.GetProperty("quantity").GetInt32());
    }

    [TestMethod]
    public async Task OrderingServiceReadsOrdersWithApiVersion2()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, Array.Empty<object>()));
        using var services = BuildServices(handler);

        await services.GetRequiredService<OrderingService>().GetOrders();

        Assert.AreEqual("?api-version=2.0", handler.Requests.Single().Uri.Query);
    }

    [TestMethod]
    public async Task QuotePriceProviderReturnsEmptyOnFailure()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var logs = new ListLoggerProvider();
        using var services = BuildServices(handler, logs: logs);

        var prices = await services.GetRequiredService<IProductPriceProvider>().GetDisplayPricesAsync([1, 2]);

        Assert.IsEmpty(prices);
        Assert.IsTrue(logs.Entries.Any(e => e.Level == LogLevel.Warning && e.Category == typeof(QuotePriceProvider).FullName),
            "Expected a warning when display prices fall back to catalog prices.");
    }

    [TestMethod]
    public async Task QuotePriceProviderUsesPerUnitPriceAfterDiscount()
    {
        var quote = Quote(
            new PricedLine(1, "Watch", null, 1, 10m, 10m, 1.235m, "Sale"),
            new PricedLine(2, "Boots", null, 1, 20m, 20m, 0m, null));
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, quote));
        using var services = BuildServices(handler);

        var prices = await services.GetRequiredService<IProductPriceProvider>().GetDisplayPricesAsync([1, 2]);

        Assert.AreEqual(8.77m, prices[1]);
        Assert.AreEqual(20m, prices[2]);

        var request = handler.Requests.Single();
        Assert.AreEqual("/api/pricing/quote", request.Uri.AbsolutePath, ignoreCase: true);
        Assert.AreEqual("?api-version=1.0", request.Uri.Query);
        using var body = JsonDocument.Parse(request.Body!);
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, items.Select(i => i.GetProperty("productId").GetInt32()).ToArray());
        Assert.IsTrue(items.All(i => i.GetProperty("units").GetInt32() == 1));
    }

    [TestMethod]
    public async Task QuotePriceProviderFallsBackWithinBudgetWhenQuoteIsSlow()
    {
        var handler = new SlowHandler(TimeSpan.FromSeconds(30));
        var logs = new ListLoggerProvider();
        using var services = BuildServices(handler, logs: logs, standardResilience: true);
        var provider = services.GetRequiredService<IProductPriceProvider>();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var prices = await provider.GetDisplayPricesAsync([1, 2]);
        stopwatch.Stop();

        Assert.IsEmpty(prices);
        Assert.IsLessThan(QuotePriceProvider.DisplayPriceBudget + TimeSpan.FromSeconds(1), stopwatch.Elapsed,
            $"Display prices took {stopwatch.Elapsed}, longer than the {QuotePriceProvider.DisplayPriceBudget} budget.");
        Assert.AreEqual(1, handler.Calls, "The display-price quote must not be retried.");
        Assert.IsTrue(logs.Entries.Any(e => e.Level == LogLevel.Warning && e.Category == typeof(QuotePriceProvider).FullName));
    }

    [TestMethod]
    public async Task QuotePriceProviderDoesNotRetryAFailedQuote()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var services = BuildServices(handler, standardResilience: true);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var prices = await services.GetRequiredService<IProductPriceProvider>().GetDisplayPricesAsync([1]);
        stopwatch.Stop();

        Assert.IsEmpty(prices);
        Assert.HasCount(1, handler.Requests);
        Assert.IsLessThan(QuotePriceProvider.DisplayPriceBudget, stopwatch.Elapsed);
    }

    [TestMethod]
    public async Task QuotePriceProviderSkipsRequestForNoProducts()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("No request expected."));
        using var services = BuildServices(handler);

        var prices = await services.GetRequiredService<IProductPriceProvider>().GetDisplayPricesAsync([]);

        Assert.IsEmpty(prices);
        Assert.IsEmpty(handler.Requests);
    }

    private static ServiceProvider BuildServices(HttpMessageHandler handler, Dictionary<string, string?>? config = null, ListLoggerProvider? logs = null,
        bool standardResilience = false)
    {
        var services = new ServiceCollection();
        if (standardResilience)
        {
            // As AddServiceDefaults does for every WebApp client.
            services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        }

        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build());
        services.AddLogging(b =>
        {
            if (logs is not null)
            {
                b.AddProvider(logs);
            }
        });

        services.AddOrderingApiClients();

        // Service discovery isn't registered here, so point the clients at a plain URL and the stub handler.
        services.AddHttpClient<OrderingService>()
            .ConfigureHttpClient(c => c.BaseAddress = new("http://ordering-api"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient<PricingService>()
            .ConfigureHttpClient(c => c.BaseAddress = new("http://ordering-api"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient(QuotePriceProvider.HttpClientName)
            .ConfigureHttpClient(c => c.BaseAddress = new("http://ordering-api"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider();
    }

    private static CreateOrderV2Request OrderRequest(decimal expectedTotal) => new(
        UserId: "user-1",
        UserName: "alice",
        City: "Redmond",
        Street: "15703 NE 61st Ct",
        State: "WA",
        Country: "U.S.",
        ZipCode: "98052",
        CardNumber: "1111222233334444",
        CardHolderName: "TESTUSER",
        CardExpiration: new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        CardSecurityNumber: "111",
        CardTypeId: 1,
        Buyer: "user-1",
        Items: [new QuoteLine(7, 2)],
        ExpectedTotal: expectedTotal);

    private static PriceQuote Quote(params PricedLine[] lines) => Quote(lines, 0m);

    private static PriceQuote Quote(PricedLine line, decimal shippingFee) => Quote([line], shippingFee);

    private static PriceQuote Quote(PricedLine[] lines, decimal shippingFee)
    {
        var subtotal = lines.Sum(l => l.Units * l.UnitPrice);
        var discount = lines.Sum(l => l.Discount);
        return new PriceQuote(lines, subtotal, discount, shippingFee, subtotal - discount + shippingFee, 42);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body, string mediaType = "application/json")
        => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, mediaType)
        };

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? Body);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                body));
            return respond(request);
        }
    }

    private sealed class SlowHandler(TimeSpan delay) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new ListLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class ListLogger(ListLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Entries)
                {
                    owner.Entries.Add(new LogEntry(category, logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
