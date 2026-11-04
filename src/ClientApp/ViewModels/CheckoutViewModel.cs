using CommunityToolkit.Mvvm.Messaging;
using eShop.ClientApp.Messages;
using eShop.ClientApp.Models.Basket;
using eShop.ClientApp.Models.Orders;
using eShop.ClientApp.Models.Pricing;
using eShop.ClientApp.Models.User;
using eShop.ClientApp.Services;
using eShop.ClientApp.Services.AppEnvironment;
using eShop.ClientApp.Services.Order;
using eShop.ClientApp.Services.Settings;
using eShop.ClientApp.ViewModels.Base;

namespace eShop.ClientApp.ViewModels;

public partial class CheckoutViewModel : ViewModelBase
{
    public const string PricesChangedMessage =
        "Prices have changed since you reviewed your order. Check the new total and place your order again.";

    private const string QuoteUnavailableMessage = "We couldn't price your order. Please, try again.";

    private readonly IAppEnvironmentService _appEnvironmentService;

    private readonly BasketViewModel _basketViewModel;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;

    [ObservableProperty] private Order _order;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Total))]
    [NotifyCanExecuteChangedFor(nameof(CheckoutCommand))]
    private PriceQuote _quote;

    [ObservableProperty] private Address _shippingAddress;

    public decimal Total => Quote?.Total ?? 0m;

    public CheckoutViewModel(
        BasketViewModel basketViewModel,
        IAppEnvironmentService appEnvironmentService, IDialogService dialogService, ISettingsService settingsService,
        INavigationService navigationService)
        : base(navigationService)
    {
        _dialogService = dialogService;
        _appEnvironmentService = appEnvironmentService;
        _settingsService = settingsService;

        _basketViewModel = basketViewModel;
    }

    public override async Task InitializeAsync()
    {
        await IsBusyFor(
            async () =>
            {
                var basketItems = _appEnvironmentService.BasketService.LocalBasketItems;

                var userInfo = await _appEnvironmentService.IdentityService.GetUserInfoAsync();

                // Create Shipping Address
                ShippingAddress = new Address
                {
                    Id = !string.IsNullOrEmpty(userInfo?.UserId) ? new Guid(userInfo.UserId) : Guid.NewGuid(),
                    Street = userInfo?.Street,
                    ZipCode = userInfo?.ZipCode,
                    State = userInfo?.State,
                    Country = userInfo?.Country,
                    City = userInfo?.Address
                };

                // Create Payment Info
                var paymentInfo = new PaymentInfo
                {
                    CardNumber = userInfo?.CardNumber,
                    CardHolderName = userInfo?.CardHolder,
                    CardType = new CardType {Id = 3, Name = "MasterCard"},
                    SecurityNumber = userInfo?.CardSecurityNumber
                };

                var orderItems = CreateOrderItems(basketItems);

                // Create new Order
                Order = new Order
                {
                    //TODO: Get a better order number generator
                    OrderNumber = (int)DateTimeOffset.Now.TimeOfDay.TotalMilliseconds,
                    UserId = userInfo.UserId,
                    UserName = userInfo.PreferredUsername,
                    OrderItems = orderItems,
                    OrderStatus = "Submitted",
                    OrderDate = DateTime.Now,
                    CardHolderName = paymentInfo.CardHolderName,
                    CardNumber = paymentInfo.CardNumber,
                    CardSecurityNumber = paymentInfo.SecurityNumber,
                    CardExpiration = DateTime.UtcNow.AddYears(5),
                    CardTypeId = paymentInfo.CardType.Id,
                    ShippingState = ShippingAddress.State,
                    ShippingCountry = ShippingAddress.Country,
                    ShippingStreet = ShippingAddress.Street,
                    ShippingCity = ShippingAddress.City,
                    ShippingZipCode = ShippingAddress.ZipCode
                };

                if (_settingsService.UseMocks)
                {
                    // Get number of orders
                    var orders = await _appEnvironmentService.OrderService.GetOrdersAsync();

                    // Create the OrderNumber
                    Order.OrderNumber = orders.Count() + 1;
                    OnPropertyChanged(nameof(Order));
                }

                try
                {
                    var quoteLines = orderItems.Select(i => new QuoteLine((int)i.ProductId, i.Quantity)).ToList();
                    ApplyQuote(await _appEnvironmentService.OrderService.GetQuoteAsync(quoteLines));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    await _dialogService.ShowAlertAsync(QuoteUnavailableMessage, "Oops!", "Ok");
                }
            });
    }

    [RelayCommand(CanExecute = nameof(CanCheckout))]
    private async Task CheckoutAsync()
    {
        try
        {
            var basket = _appEnvironmentService.OrderService.MapOrderToBasket(Order);
            basket.RequestId = Guid.NewGuid();

            var result = await _appEnvironmentService.OrderService.CreateOrderAsync(Order, Quote.Total);

            if (result is CreateOrderResult.PricesChanged pricesChanged)
            {
                ApplyQuote(pricesChanged.Quote);
                await _dialogService.ShowAlertAsync(PricesChangedMessage, "Checkout", "Ok");
                return;
            }

            // Clean Basket
            await _appEnvironmentService.BasketService.ClearBasketAsync();

            // Reset Basket badge
            await _basketViewModel.ClearBasketItems();
            
            WeakReferenceMessenger.Default
                .Send(new ProductCountChangedMessage(0));

            // Navigate to Orders
            await NavigationService.NavigateToAsync("//Main/Catalog");

            // Show Dialog
            await _dialogService.ShowAlertAsync("Order sent successfully!", "Checkout", "Ok");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            await _dialogService.ShowAlertAsync("An error ocurred. Please, try again.", "Oops!", "Ok");
        }
    }

    private bool CanCheckout() => Quote is not null;

    private void ApplyQuote(PriceQuote quote)
    {
        foreach (var line in quote.Lines)
        {
            var orderItem = Order.OrderItems.FirstOrDefault(i => i.ProductId == line.ProductId);

            if (orderItem is not null)
            {
                orderItem.UnitPrice = line.UnitPrice;
                orderItem.Discount = line.Discount;
            }
        }

        Order.Total = quote.Total;
        Quote = quote;
        OnPropertyChanged(nameof(Order));
    }

    private static List<OrderItem> CreateOrderItems(IEnumerable<BasketItem> basketItems)
    {
        var orderItems = new List<OrderItem>();

        foreach (var basketItem in basketItems)
        {
            if (!string.IsNullOrEmpty(basketItem.ProductName))
            {
                orderItems.Add(new OrderItem
                {
                    OrderId = null,
                    ProductId = basketItem.ProductId,
                    ProductName = basketItem.ProductName,
                    PictureUrl = basketItem.PictureUrl,
                    Quantity = basketItem.Quantity,
                    UnitPrice = basketItem.UnitPrice
                });
            }
        }

        return orderItems;
    }
}
