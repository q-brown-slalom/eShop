using ClientApp.UnitTests.Mocks;
using eShop.ClientApp.Models.Basket;
using eShop.ClientApp.Models.Pricing;
using eShop.ClientApp.Services.Identity;

namespace ClientApp.UnitTests.ViewModels;

[TestClass]
public class CheckoutViewModelTests
{
    private const string PricesChangedMessage =
        "Prices have changed since you reviewed your order. Check the new total and place your order again.";

    private readonly MockNavigationService _navigationService = new();
    private readonly MockSettingsService _settingsService = new();
    private readonly MockDialogService _dialogService = new();
    private readonly BasketMockService _basketService = new();
    private readonly IdentityMockService _identityService = new();
    private readonly MockOrderService _orderService = new()
    {
        Quote = MockOrderService.QuoteOf(42.42m,
            new PricedLine(1, "Mug", null, 2, 20m, 18m, 0m, null),
            new PricedLine(4, "Cup", null, 1, 7m, 6.42m, 0m, "Sale"))
    };
    private readonly AppEnvironmentService _appEnvironmentService;

    public CheckoutViewModelTests()
    {
        var catalogService = new CatalogMockService();
        _appEnvironmentService = new AppEnvironmentService(
            _basketService, _basketService,
            catalogService, catalogService,
            _orderService, _orderService,
            _identityService, _identityService);
        _appEnvironmentService.UpdateDependencies(true);

        _basketService.LocalBasketItems =
        [
            new BasketItem { Id = "1", ProductId = 1, ProductName = "Mug", Quantity = 2, UnitPrice = 20m },
            new BasketItem { Id = "2", ProductId = 4, ProductName = "Cup", Quantity = 1, UnitPrice = 7m }
        ];
    }

    [TestMethod]
    public async Task CheckoutUsesQuotedTotal()
    {
        var viewModel = await CreateInitializedViewModelAsync();

        Assert.AreEqual(42.42m, viewModel.Total);
        Assert.AreEqual(42.42m, viewModel.Order.Total);
        CollectionAssert.AreEqual(new[] { new QuoteLine(1, 2), new QuoteLine(4, 1) }, _orderService.QuotedLines);
        Assert.AreEqual(6.42m, viewModel.Order.OrderItems.Single(i => i.ProductId == 4).UnitPrice);
    }

    [TestMethod]
    public async Task CheckoutSubmitsQuotedTotalAndClearsBasket()
    {
        var viewModel = await CreateInitializedViewModelAsync();

        await viewModel.CheckoutCommand.ExecuteUntilComplete();

        Assert.AreEqual(42.42m, _orderService.SubmittedExpectedTotal);
        Assert.IsNull(_basketService.LocalBasketItems);
        CollectionAssert.DoesNotContain(_dialogService.Messages, PricesChangedMessage);
    }

    [TestMethod]
    public async Task CheckoutOnPricesChangedShowsDialogAndKeepsBasket()
    {
        var viewModel = await CreateInitializedViewModelAsync();
        _orderService.CreateResult = new CreateOrderResult.PricesChanged(MockOrderService.QuoteOf(50m,
            new PricedLine(1, "Mug", null, 2, 22m, 22m, 0m, null),
            new PricedLine(4, "Cup", null, 1, 7m, 6m, 0m, null)));

        await viewModel.CheckoutCommand.ExecuteUntilComplete();

        CollectionAssert.AreEqual(new[] { PricesChangedMessage }, _dialogService.Messages);
        Assert.AreEqual(50m, viewModel.Total);
        Assert.AreEqual(50m, viewModel.Order.Total);
        Assert.IsNotNull(_basketService.LocalBasketItems);
        Assert.HasCount(2, (await _basketService.GetBasketAsync()).Items);

        await viewModel.CheckoutCommand.ExecuteUntilComplete();

        Assert.AreEqual(50m, _orderService.SubmittedExpectedTotal);
    }

    private async Task<CheckoutViewModel> CreateInitializedViewModelAsync()
    {
        await _identityService.SignInAsync();
        var basketViewModel = new BasketViewModel(_appEnvironmentService, _navigationService, _settingsService);
        var viewModel = new CheckoutViewModel(basketViewModel, _appEnvironmentService, _dialogService,
            _settingsService, _navigationService);

        await viewModel.InitializeAsync();

        return viewModel;
    }
}
