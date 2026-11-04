using eShop.ClientApp.Models.Pricing;

namespace eShop.ClientApp.Services.Order;

public abstract record CreateOrderResult
{
    private CreateOrderResult()
    {
    }

    public sealed record Created : CreateOrderResult;

    public sealed record PricesChanged(PriceQuote Quote) : CreateOrderResult;
}
