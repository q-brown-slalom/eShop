using eShop.ClientApp.Models.Basket;
using eShop.ClientApp.Models.Pricing;

namespace eShop.ClientApp.Services.Order;

public interface IOrderService
{
    Task<PriceQuote> GetQuoteAsync(IEnumerable<QuoteLine> lines);

    Task<CreateOrderResult> CreateOrderAsync(Models.Orders.Order newOrder, decimal expectedTotal);

    Task<IEnumerable<Models.Orders.Order>> GetOrdersAsync();

    Task<Models.Orders.Order> GetOrderAsync(int orderId);

    Task<bool> CancelOrderAsync(int orderId);

    OrderCheckout MapOrderToBasket(Models.Orders.Order order);
}
