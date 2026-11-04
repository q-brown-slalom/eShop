using eShop.BusinessRules.Client.Pricing;
using eShop.WebAppComponents.Services;

namespace eShop.WebApp.Services;

/// <summary>
/// Display prices for catalog and item pages. They must render even when Ordering.API is down or slow, so the quote
/// gets a short budget on a client without retries, and any failure falls back to catalog prices (D7).
/// </summary>
public class QuotePriceProvider(IHttpClientFactory httpClientFactory, ILogger<QuotePriceProvider> logger) : IProductPriceProvider
{
    public const string HttpClientName = "ordering-display-prices";

    /// <summary>The longest a page waits for display prices before showing catalog prices.</summary>
    public static readonly TimeSpan DisplayPriceBudget = TimeSpan.FromSeconds(2);

    public async Task<IReadOnlyDictionary<int, decimal>> GetDisplayPricesAsync(IReadOnlyCollection<int> productIds)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<int, decimal>();
        }

        using var budget = new CancellationTokenSource(DisplayPriceBudget);
        try
        {
            var pricingService = new PricingService(httpClientFactory.CreateClient(HttpClientName));
            var quote = await pricingService.GetQuoteAsync(productIds.Distinct().Select(id => new QuoteLine(id, 1)).ToList(), budget.Token);
            return quote.Lines
                .Where(line => line.Units > 0)
                .ToDictionary(line => line.ProductId, line => PriceRounding.Round(line.LineTotal / line.Units));
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            logger.LogWarning("Display prices took longer than {Budget}; showing catalog prices instead", DisplayPriceBudget);
            return new Dictionary<int, decimal>();
        }
        catch (Exception ex)
        {
            // Catalog and item pages must still render, so they fall back to catalog prices (D7).
            logger.LogWarning(ex, "Could not get display prices; showing catalog prices instead");
            return new Dictionary<int, decimal>();
        }
    }
}
