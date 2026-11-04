using eShop.ClientApp.Models.Pricing;

namespace ClientApp.UnitTests.Mocks;

public class MockOrderService : OrderMockService, IOrderService
{
    public required PriceQuote Quote { get; set; }

    public CreateOrderResult CreateResult { get; set; } = new CreateOrderResult.Created();

    public List<QuoteLine> QuotedLines { get; } = [];

    public decimal? SubmittedExpectedTotal { get; private set; }

    public new Task<PriceQuote> GetQuoteAsync(IEnumerable<QuoteLine> lines)
    {
        QuotedLines.Clear();
        QuotedLines.AddRange(lines);
        return Task.FromResult(Quote);
    }

    public new Task<CreateOrderResult> CreateOrderAsync(eShop.ClientApp.Models.Orders.Order newOrder, decimal expectedTotal)
    {
        SubmittedExpectedTotal = expectedTotal;
        return Task.FromResult(CreateResult);
    }

    public static PriceQuote QuoteOf(decimal total, params PricedLine[] lines) =>
        new(lines, total, 0m, 0m, total, 1);
}
