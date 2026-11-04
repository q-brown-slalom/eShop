namespace eShop.ClientApp.Models.Pricing;

/// <summary>The parts of the 409 "prices-changed" ProblemDetails the app uses.</summary>
public sealed record PricesChangedProblem(string Type, PriceQuote Quote)
{
    public const string ProblemType = "https://eshop/problems/prices-changed";
}
