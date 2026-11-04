using eShop.BusinessRules.Client.Pricing;

namespace eShop.WebApp.Services;

public class PricingService(HttpClient httpClient)
{
    private const string QuoteUrl = "/api/pricing/quote";

    public async Task<PriceQuote> GetQuoteAsync(IReadOnlyList<QuoteLine> lines, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(QuoteUrl, new PriceQuoteRequest(lines), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PriceQuote>(cancellationToken)
            ?? throw new InvalidOperationException("The pricing quote response was empty.");
    }

    private record PriceQuoteRequest(IReadOnlyList<QuoteLine> Items);
}
