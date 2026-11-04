using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using eShop.WebAppComponents.Catalog;
using eShop.WebAppComponents.Services;
using eShop.BusinessRules.Client.Pricing;

namespace eShop.WebApp.Services;

public class BasketState(
    BasketService basketService,
    CatalogService catalogService,
    OrderingService orderingService,
    PricingService pricingService,
    AuthenticationStateProvider authenticationStateProvider) : IBasketState
{
    private Task<IReadOnlyCollection<BasketItem>>? _cachedBasket;
    private Task<PriceQuote?>? _cachedQuote;
    private HashSet<BasketStateChangedSubscription> _changeSubscriptions = new();

    public async Task DeleteBasketAsync()
    {
        await basketService.DeleteBasketAsync();
        InvalidateCache();
    }

    public async Task<IReadOnlyCollection<BasketItem>> GetBasketItemsAsync()
        => (await GetUserAsync()).Identity?.IsAuthenticated == true
        ? await FetchBasketItemsAsync()
        : [];

    /// <summary>
    /// Prices the basket on the server. Returns null for an empty basket; throws when no quote can be obtained,
    /// because the cart and checkout must only show server-quoted totals. Also fills each
    /// <see cref="BasketItem.UnitPrice"/> from its quote line.
    /// </summary>
    public async Task<PriceQuote?> GetQuoteAsync()
        => (await GetUserAsync()).Identity?.IsAuthenticated == true
        ? await FetchQuoteAsync()
        : null;

    public IDisposable NotifyOnChange(EventCallback callback)
    {
        var subscription = new BasketStateChangedSubscription(this, callback);
        _changeSubscriptions.Add(subscription);
        return subscription;
    }

    public async Task AddAsync(CatalogItem item)
    {
        var items = (await FetchBasketItemsAsync()).Select(i => new BasketQuantity(i.ProductId, i.Quantity)).ToList();
        bool found = false;
        for (var i = 0; i < items.Count; i++)
        {
            var existing = items[i];
            if (existing.ProductId == item.Id)
            {
                items[i] = existing with { Quantity = existing.Quantity + 1 };
                found = true;
                break;
            }
        }

        if (!found)
        {
            items.Add(new BasketQuantity(item.Id, 1));
        }

        InvalidateCache();
        await basketService.UpdateBasketAsync(items);
        await NotifyChangeSubscribersAsync();
    }

    public async Task SetQuantityAsync(int productId, int quantity)
    {
        var existingItems = (await FetchBasketItemsAsync()).ToList();
        if (existingItems.FirstOrDefault(row => row.ProductId == productId) is { } row)
        {
            if (quantity > 0)
            {
                row.Quantity = quantity;
            }
            else
            {
                existingItems.Remove(row);
            }

            InvalidateCache();
            await basketService.UpdateBasketAsync(existingItems.Select(i => new BasketQuantity(i.ProductId, i.Quantity)).ToList());
            await NotifyChangeSubscribersAsync();
        }
    }

    public async Task CheckoutAsync(BasketCheckoutInfo checkoutInfo)
    {
        if (checkoutInfo.RequestId == default)
        {
            checkoutInfo.RequestId = Guid.NewGuid();
        }

        var buyerId = await authenticationStateProvider.GetBuyerIdAsync() ?? throw new InvalidOperationException("User does not have a buyer ID");
        var userName = await authenticationStateProvider.GetUserNameAsync() ?? throw new InvalidOperationException("User does not have a user name");

        var orderItems = await FetchBasketItemsAsync();

        // Ordering.API prices the items itself and only creates the order if its total still equals the one
        // shown at checkout. Otherwise it throws PricesChangedException and the basket is kept.
        var request = new CreateOrderV2Request(
            UserId: buyerId,
            UserName: userName,
            City: checkoutInfo.City!,
            Street: checkoutInfo.Street!,
            State: checkoutInfo.State!,
            Country: checkoutInfo.Country!,
            ZipCode: checkoutInfo.ZipCode!,
            CardNumber: "1111222233334444",
            CardHolderName: "TESTUSER",
            CardExpiration: DateTime.UtcNow.AddYears(1),
            CardSecurityNumber: "111",
            CardTypeId: checkoutInfo.CardTypeId,
            Buyer: buyerId,
            Items: [.. orderItems.Select(i => new QuoteLine(i.ProductId, i.Quantity))],
            ExpectedTotal: checkoutInfo.ExpectedTotal);
        await orderingService.CreateOrder(request, checkoutInfo.RequestId);
        await DeleteBasketAsync();
    }

    private void InvalidateCache()
    {
        _cachedBasket = null;
        _cachedQuote = null;
    }

    private Task NotifyChangeSubscribersAsync()
        => Task.WhenAll(_changeSubscriptions.Select(s => s.NotifyAsync()));

    private async Task<PriceQuote?> FetchQuoteAsync()
    {
        var fetch = _cachedQuote ??= FetchCoreAsync();
        try
        {
            return await fetch;
        }
        catch when (_cachedQuote == fetch)
        {
            // Don't keep a failed quote cached; the next call tries again.
            _cachedQuote = null;
            throw;
        }

        async Task<PriceQuote?> FetchCoreAsync()
        {
            var items = await FetchBasketItemsAsync();
            if (items.Count == 0)
            {
                return null;
            }

            var quote = await pricingService.GetQuoteAsync([.. items.Select(i => new QuoteLine(i.ProductId, i.Quantity))]);
            var lines = quote.Lines.ToDictionary(l => l.ProductId);
            foreach (var item in items)
            {
                if (lines.TryGetValue(item.ProductId, out var line))
                {
                    item.UnitPrice = line.UnitPrice;
                }
            }

            return quote;
        }
    }

    private async Task<ClaimsPrincipal> GetUserAsync()
        => (await authenticationStateProvider.GetAuthenticationStateAsync()).User;

    private Task<IReadOnlyCollection<BasketItem>> FetchBasketItemsAsync()
    {
        return _cachedBasket ??= FetchCoreAsync();

        async Task<IReadOnlyCollection<BasketItem>> FetchCoreAsync()
        {
            var quantities = await basketService.GetBasketAsync();
            if (quantities.Count == 0)
            {
                return [];
            }

            // Get details for the items in the basket
            var basketItems = new List<BasketItem>();
            var productIds = quantities.Select(row => row.ProductId);
            var catalogItems = (await catalogService.GetCatalogItems(productIds)).ToDictionary(k => k.Id, v => v);
            foreach (var item in quantities)
            {
                var catalogItem = catalogItems[item.ProductId];
                var orderItem = new BasketItem
                {
                    Id = Guid.NewGuid().ToString(), // TODO: this value is meaningless, use ProductId instead.
                    ProductId = catalogItem.Id,
                    ProductName = catalogItem.Name,
                    UnitPrice = catalogItem.Price,
                    Quantity = item.Quantity,
                };
                basketItems.Add(orderItem);
            }

            return basketItems;
        }
    }

    private class BasketStateChangedSubscription(BasketState Owner, EventCallback Callback) : IDisposable
    {
        public Task NotifyAsync() => Callback.InvokeAsync();
        public void Dispose() => Owner._changeSubscriptions.Remove(this);
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
