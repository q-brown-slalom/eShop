using eShop.BusinessRules.Client.Pricing;
using eShop.Ordering.API.Application.Pricing;
using Microsoft.AspNetCore.Http.HttpResults;
using CardType = eShop.Ordering.API.Application.Queries.CardType;
using Order = eShop.Ordering.API.Application.Queries.Order;

public static class OrdersApi
{
    public const string PricesChangedProblemType = "https://eshop/problems/prices-changed";

    public static RouteGroupBuilder MapOrdersApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("api/orders").HasApiVersion(1.0);

        MapSharedRoutes(api);
        api.MapPost("/", CreateOrderAsync);

        return api;
    }

    // v2 maps every v1 route so clients can move their whole ordering client to api-version=2.0 (D6).
    public static RouteGroupBuilder MapOrdersApiV2(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("api/orders").HasApiVersion(2.0);

        MapSharedRoutes(api);
        api.MapPost("/", CreateOrderV2Async)
            .WithName("CreateOrderV2")
            .WithSummary("Create an order at server prices")
            .WithDescription($"Prices the items server-side and creates the order only when the priced total equals `expectedTotal` (rounded to 2 decimals). Otherwise returns 409 ProblemDetails of type `{PricesChangedProblemType}` with the fresh quote in `quote` and records nothing, so the same `x-requestid` can be retried. Returns 400 listing unknown product ids, and 503 when the catalog is unavailable.")
            .WithTags("Orders");

        return api;
    }

    private static void MapSharedRoutes(RouteGroupBuilder api)
    {
        api.MapPut("/cancel", CancelOrderAsync);
        api.MapPut("/ship", ShipOrderAsync);
        api.MapGet("{orderId:int}", GetOrderAsync);
        api.MapGet("/", GetOrdersByUserAsync);
        api.MapGet("/cardtypes", GetCardTypesAsync);
        api.MapPost("/draft", CreateOrderDraftAsync);
    }

    public static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> CancelOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        CancelOrderCommand command,
        [AsParameters] OrderServices services)
    {
        if (requestId == Guid.Empty)
        {
            return TypedResults.BadRequest("Empty GUID is not valid for request ID");
        }

        var requestCancelOrder = new IdentifiedCommand<CancelOrderCommand, bool>(command, requestId);

        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            requestCancelOrder.GetGenericTypeName(),
            nameof(requestCancelOrder.Command.OrderNumber),
            requestCancelOrder.Command.OrderNumber,
            requestCancelOrder);

        var commandResult = await services.Mediator.Send(requestCancelOrder);

        if (!commandResult)
        {
            return TypedResults.Problem(detail: "Cancel order failed to process.", statusCode: 500);
        }

        return TypedResults.Ok();
    }

    public static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> ShipOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        ShipOrderCommand command,
        [AsParameters] OrderServices services)
    {
        if (requestId == Guid.Empty)
        {
            return TypedResults.BadRequest("Empty GUID is not valid for request ID");
        }

        var requestShipOrder = new IdentifiedCommand<ShipOrderCommand, bool>(command, requestId);

        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            requestShipOrder.GetGenericTypeName(),
            nameof(requestShipOrder.Command.OrderNumber),
            requestShipOrder.Command.OrderNumber,
            requestShipOrder);

        var commandResult = await services.Mediator.Send(requestShipOrder);

        if (!commandResult)
        {
            return TypedResults.Problem(detail: "Ship order failed to process.", statusCode: 500);
        }

        return TypedResults.Ok();
    }

    public static async Task<Results<Ok<Order>, NotFound>> GetOrderAsync(int orderId, [AsParameters] OrderServices services)
    {
        try
        {
            var order = await services.Queries.GetOrderAsync(orderId);
            return TypedResults.Ok(order);
        }
        catch
        {
            return TypedResults.NotFound();
        }
    }

    public static async Task<Ok<IEnumerable<OrderSummary>>> GetOrdersByUserAsync([AsParameters] OrderServices services)
    {
        var userId = services.IdentityService.GetUserIdentity();
        var orders = await services.Queries.GetOrdersFromUserAsync(userId);
        return TypedResults.Ok(orders);
    }

    public static async Task<Ok<IEnumerable<CardType>>> GetCardTypesAsync(IOrderQueries orderQueries)
    {
        var cardTypes = await orderQueries.GetCardTypesAsync();
        return TypedResults.Ok(cardTypes);
    }

    public static async Task<OrderDraftDTO> CreateOrderDraftAsync(CreateOrderDraftCommand command, [AsParameters] OrderServices services)
    {
        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            command.GetGenericTypeName(),
            nameof(command.BuyerId),
            command.BuyerId,
            command);

        return await services.Mediator.Send(command);
    }

    public static async Task<Results<Ok, BadRequest<string>, ValidationProblem, ProblemHttpResult>> CreateOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        CreateOrderRequest request,
        [AsParameters] OrderServices services,
        IOrderPricingService pricing,
        IApiUsageRecorder usage,
        OrderingMetrics metrics,
        CancellationToken cancellationToken)
    {
        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId}",
            nameof(CreateOrderRequest),
            nameof(request.UserId),
            request?.UserId); //don't log the request as it has CC number

        if (requestId == Guid.Empty)
        {
            services.Logger.LogWarning("Create order request rejected because the request id is missing");
            return TypedResults.BadRequest("RequestId is missing.");
        }

        // v1 clients send prices, but they are ignored: the order is charged the server quote.
        var lines = request?.Items?.Select(i => i is null ? null : new QuoteLine(i.ProductId, i.Quantity)).ToList();
        var errors = PricingApi.ValidateItems(lines);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        PriceQuote quote;
        try
        {
            quote = await pricing.QuoteAsync(lines, cancellationToken);
        }
        catch (UnknownProductsException ex)
        {
            return PricingApi.UnknownProducts(ex);
        }
        catch (CatalogUnavailableException ex)
        {
            services.Logger.LogWarning(ex, "Create order failed because catalog prices are unavailable");
            return PricingApi.CatalogUnavailable();
        }

        var command = CreateCommand(WithClientPictureUrls(quote, request.Items), request.UserId, request.UserName, request.City, request.Street,
            request.State, request.Country, request.ZipCode, request.CardNumber, request.CardHolderName, request.CardExpiration,
            request.CardSecurityNumber, request.CardTypeId);

        if (await SendCreateOrderAsync(requestId, command, services))
        {
            metrics.CreateOrderV1Called();
            await RecordCreateOrderV1UsageAsync(usage, services.Logger);
        }

        return TypedResults.Ok();
    }

    public static async Task<Results<Ok, BadRequest<string>, ValidationProblem, ProblemHttpResult>> CreateOrderV2Async(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        CreateOrderV2Request request,
        [AsParameters] OrderServices services,
        IOrderPricingService pricing,
        OrderingMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            services.Logger.LogWarning("Create order request rejected because the request id is missing");
            return TypedResults.BadRequest("RequestId is missing.");
        }

        var errors = PricingApi.ValidateItems(request?.Items);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        PriceQuote quote;
        try
        {
            quote = await pricing.QuoteAsync(request.Items, cancellationToken);
        }
        catch (UnknownProductsException ex)
        {
            return PricingApi.UnknownProducts(ex);
        }
        catch (CatalogUnavailableException ex)
        {
            services.Logger.LogWarning(ex, "Create order failed because catalog prices are unavailable");
            return PricingApi.CatalogUnavailable();
        }

        // Nothing is sent to the mediator on a mismatch, so no idempotency record is written and the
        // client can retry with the same x-requestid once it has shown the fresh total.
        if (quote.Total != PriceRounding.Round(request.ExpectedTotal))
        {
            metrics.TotalMismatch();
            using (services.Logger.BeginScope(new List<KeyValuePair<string, object>> { new("rules.snapshot.version", quote.SnapshotVersion) }))
            {
                services.Logger.LogInformation("Create order rejected because the expected total no longer matches the priced total");
            }

            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                type: PricesChangedProblemType,
                title: "Prices changed",
                detail: "The order total changed since it was shown. Review the new quote and retry with its total.",
                extensions: new Dictionary<string, object> { ["quote"] = quote });
        }

        var command = CreateCommand(quote, request.UserId, request.UserName, request.City, request.Street,
            request.State, request.Country, request.ZipCode, request.CardNumber, request.CardHolderName, request.CardExpiration,
            request.CardSecurityNumber, request.CardTypeId);

        if (!await SendCreateOrderAsync(requestId, command, services))
        {
            return TypedResults.Problem(detail: "Create order failed to process.", statusCode: StatusCodes.Status500InternalServerError);
        }

        return TypedResults.Ok();
    }

    private static CreateOrderCommand CreateCommand(PriceQuote quote, string userId, string userName, string city, string street,
        string state, string country, string zipCode, string cardNumber, string cardHolderName, DateTime cardExpiration,
        string cardSecurityNumber, int cardTypeId)
    {
        //mask the credit card number
        var maskedCCNumber = cardNumber is { Length: >= 4 }
            ? cardNumber.Substring(cardNumber.Length - 4).PadLeft(cardNumber.Length, 'X')
            : cardNumber;

        return new CreateOrderCommand(quote, userId, userName, city, street, state, country, zipCode,
            maskedCCNumber, cardHolderName, cardExpiration, cardSecurityNumber, cardTypeId);
    }

    // Catalog prices carry no picture URL, so v1 keeps storing the basket's picture URL as it did before server pricing.
    private static PriceQuote WithClientPictureUrls(PriceQuote quote, List<BasketItem> items)
    {
        var pictures = items
            .Where(i => i is not null && !string.IsNullOrEmpty(i.PictureUrl))
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.First().PictureUrl);

        return quote with
        {
            Lines = quote.Lines
                .Select(l => l.PictureUrl is null && pictures.TryGetValue(l.ProductId, out var url) ? l with { PictureUrl = url } : l)
                .ToList()
        };
    }

    private static async Task<bool> SendCreateOrderAsync(Guid requestId, CreateOrderCommand command, OrderServices services)
    {
        using (services.Logger.BeginScope(new List<KeyValuePair<string, object>> { new("rules.snapshot.version", command.PricingSnapshotVersion) }))
        {
            var requestCreateOrder = new IdentifiedCommand<CreateOrderCommand, bool>(command, requestId);

            // The command is not logged here or by the MediatR pipeline: it carries the card holder and address.
            services.Logger.LogInformation(
                "Sending command: {CommandName} - {IdProperty}: {CommandId}",
                requestCreateOrder.GetGenericTypeName(),
                nameof(requestCreateOrder.Id),
                requestCreateOrder.Id);

            var result = await services.Mediator.Send(requestCreateOrder);

            if (result)
            {
                services.Logger.LogInformation("CreateOrderCommand succeeded - RequestId: {RequestId}", requestId);
            }
            else
            {
                services.Logger.LogWarning("CreateOrderCommand failed - RequestId: {RequestId}", requestId);
            }

            return result;
        }
    }

    // Runs after the order's transaction has committed. The publish guard relies on this count, but a recording
    // failure must never fail an order that was already created. CancellationToken.None keeps a client disconnect
    // from dropping the count.
    private static async Task RecordCreateOrderV1UsageAsync(IApiUsageRecorder usage, ILogger logger)
    {
        try
        {
            await usage.RecordAsync(IApiUsageRecorder.CreateOrderV1Operation, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record create-order v1 usage");
        }
    }
}

public record CreateOrderRequest(
    string UserId,
    string UserName,
    string City,
    string Street,
    string State,
    string Country,
    string ZipCode,
    string CardNumber,
    string CardHolderName,
    DateTime CardExpiration,
    string CardSecurityNumber,
    int CardTypeId,
    string Buyer,
    List<BasketItem> Items);

public record CreateOrderV2Request(
    string UserId,
    string UserName,
    string City,
    string Street,
    string State,
    string Country,
    string ZipCode,
    string CardNumber,
    string CardHolderName,
    DateTime CardExpiration,
    string CardSecurityNumber,
    int CardTypeId,
    string Buyer,
    List<QuoteLine> Items,
    decimal ExpectedTotal);
