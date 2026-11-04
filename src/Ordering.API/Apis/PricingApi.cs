using eShop.BusinessRules.Client.Pricing;
using eShop.Ordering.API.Application.Pricing;
using Microsoft.AspNetCore.Http.HttpResults;

public static class PricingApi
{
    public const int MinLines = 1;
    public const int MaxLines = 100;
    public const int MinUnits = 1;
    public const int MaxUnits = 10_000;

    public static RouteGroupBuilder MapPricingApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("api/pricing").HasApiVersion(1.0);

        api.MapPost("/quote", QuoteAsync)
            .WithName("QuotePrices")
            .WithSummary("Price a basket")
            .WithDescription($"Prices {MinLines}-{MaxLines} lines ({MinUnits}-{MaxUnits} units each) at current catalog prices with the published business rules. Lines for the same product are merged. Anonymous callers are allowed. Returns 400 listing unknown product ids, and 503 when the catalog is unavailable.")
            .WithTags("Pricing");

        return api;
    }

    public static async Task<Results<Ok<PriceQuote>, ValidationProblem, ProblemHttpResult>> QuoteAsync(
        PriceQuoteRequest request,
        IOrderPricingService pricing,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        try
        {
            return TypedResults.Ok(await pricing.QuoteAsync(request.Items, cancellationToken));
        }
        catch (UnknownProductsException ex)
        {
            return UnknownProducts(ex);
        }
        catch (CatalogUnavailableException ex)
        {
            loggerFactory.CreateLogger(typeof(PricingApi)).LogWarning(ex, "Quote failed because catalog prices are unavailable");
            return CatalogUnavailable();
        }
    }

    internal static ValidationProblem UnknownProducts(UnknownProductsException ex)
        => TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["items"] = [$"Unknown product ids: {string.Join(", ", ex.ProductIds)}."]
        });

    internal static ProblemHttpResult CatalogUnavailable()
        => TypedResults.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Catalog unavailable",
            detail: "Prices could not be retrieved from the catalog. Try again later.");

    private static Dictionary<string, string[]> Validate(PriceQuoteRequest request)
        // Ordering.API has nullable reference types off, so an empty or "null" body binds as null.
        => ValidateItems(request?.Items);

    /// <summary>Checks the line count and per-line units shared by quotes and create-order requests.</summary>
    internal static Dictionary<string, string[]> ValidateItems(IReadOnlyList<QuoteLine> items)
    {
        var errors = new Dictionary<string, string[]>();

        if (items is null || items.Count is < MinLines or > MaxLines)
        {
            errors["items"] = [$"items must contain between {MinLines} and {MaxLines} lines."];
            return errors;
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is null)
            {
                errors[$"items[{i}]"] = ["Line is required."];
            }
            else if (items[i].Units is < MinUnits or > MaxUnits)
            {
                errors[$"items[{i}].units"] = [$"units must be between {MinUnits} and {MaxUnits}."];
            }
        }

        return errors;
    }
}

public record PriceQuoteRequest(List<QuoteLine> Items);
