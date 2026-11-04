using eShop.BusinessRules.Client.Pricing;
using eShop.WebAppComponents.Services;

namespace eShop.WebApp.Services;

public class QuotePriceProvider(PricingService pricingService, ILogger<QuotePriceProvider> logger) : IProductPriceProvider
{
    public async Task<IReadOnlyDictionary<int, decimal>> GetDisplayPricesAsync(IReadOnlyCollection<int> productIds)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<int, decimal>();
        }

        try
        {
            var quote = await pricingService.GetQuoteAsync(productIds.Distinct().Select(id => new QuoteLine(id, 1)).ToList());
            return quote.Lines
                .Where(line => line.Units > 0)
                .ToDictionary(line => line.ProductId, line => PriceRounding.Round(line.LineTotal / line.Units));
        }
        catch (Exception ex)
        {
            // Catalog and item pages must still render, so they fall back to catalog prices (D7).
            logger.LogWarning(ex, "Could not get display prices; showing catalog prices instead");
            return new Dictionary<int, decimal>();
        }
    }
}
