using eShop.BusinessRules.Client.Pricing;
using eShop.EventBus.Events;
using eShop.Ordering.API.Application.IntegrationEvents;
using eShop.Ordering.API.Application.IntegrationEvents.Events;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

namespace eShop.Ordering.UnitTests.Application;

[TestClass]
public class NewOrderRequestHandlerTest
{
    private readonly IOrderRepository _orderRepositoryMock;
    private readonly IIdentityService _identityServiceMock;
    private readonly IMediator _mediator;
    private readonly IOrderingIntegrationEventService _orderingIntegrationEventService;

    public NewOrderRequestHandlerTest()
    {

        _orderRepositoryMock = Substitute.For<IOrderRepository>();
        _identityServiceMock = Substitute.For<IIdentityService>();
        _orderingIntegrationEventService = Substitute.For<IOrderingIntegrationEventService>();
        _mediator = Substitute.For<IMediator>();
    }

    [TestMethod]
    public async Task Handle_return_false_if_order_is_not_persisted()
    {
        var buyerId = "1234";

        var fakeOrderCmd = FakeOrderRequestWithBuyer(new Dictionary<string, object>
        { ["cardExpiration"] = DateTime.UtcNow.AddYears(1) });

        _orderRepositoryMock.GetAsync(Arg.Any<int>())
            .Returns(Task.FromResult(FakeOrder()));

        _orderRepositoryMock.UnitOfWork.SaveChangesAsync(default)
            .Returns(Task.FromResult(1));

        _identityServiceMock.GetUserIdentity().Returns(buyerId);

        var LoggerMock = Substitute.For<ILogger<CreateOrderCommandHandler>>();
        //Act
        var handler = new CreateOrderCommandHandler(_mediator, _orderingIntegrationEventService, _orderRepositoryMock, _identityServiceMock, LoggerMock);
        var cltToken = new CancellationToken();
        var result = await handler.Handle(fakeOrderCmd, cltToken);

        //Assert
        Assert.IsFalse(result);
        await _orderingIntegrationEventService.DidNotReceive().AddAndSaveEventAsync(Arg.Any<OrderStartedIntegrationEvent>());
    }

    [TestMethod]
    public async Task Handle_adds_order_started_event_only_after_order_is_saved()
    {
        var command = FakeOrderRequestWithBuyer(new Dictionary<string, object> { ["cardExpiration"] = DateTime.UtcNow.AddYears(1) }, ValidQuote());
        _orderRepositoryMock.UnitOfWork.SaveEntitiesAsync(Arg.Any<CancellationToken>()).Returns(true);

        var handler = new CreateOrderCommandHandler(_mediator, _orderingIntegrationEventService, _orderRepositoryMock, _identityServiceMock, Substitute.For<ILogger<CreateOrderCommandHandler>>());
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.IsTrue(result);
        Received.InOrder(() =>
        {
            _orderRepositoryMock.UnitOfWork.SaveEntitiesAsync(Arg.Any<CancellationToken>());
            _orderingIntegrationEventService.AddAndSaveEventAsync(Arg.Any<OrderStartedIntegrationEvent>());
        });
    }

    [TestMethod]
    public async Task Handle_adds_no_order_started_event_when_order_breaks_an_invariant()
    {
        // A catalog price of 0.00 breaks the minimum unit price invariant while the order is built.
        var quote = new PriceQuote(
            [new PricedLine(1, "free cup", null, 1, BasePrice: 0m, UnitPrice: 0m, Discount: 0m, PromotionLabel: null)],
            Subtotal: 0m, DiscountTotal: 0m, ShippingFee: 0m, Total: 0m, SnapshotVersion: 0);
        var command = FakeOrderRequestWithBuyer(new Dictionary<string, object> { ["cardExpiration"] = DateTime.UtcNow.AddYears(1) }, quote);

        var handler = new CreateOrderCommandHandler(_mediator, _orderingIntegrationEventService, _orderRepositoryMock, _identityServiceMock, Substitute.For<ILogger<CreateOrderCommandHandler>>());

        await Assert.ThrowsExactlyAsync<OrderingDomainException>(() => handler.Handle(command, CancellationToken.None));
        await _orderingIntegrationEventService.DidNotReceive().AddAndSaveEventAsync(Arg.Any<IntegrationEvent>());
        _orderRepositoryMock.DidNotReceive().Add(Arg.Any<Order>());
    }

    [TestMethod]
    public async Task Handle_adds_no_order_started_event_when_save_throws()
    {
        var command = FakeOrderRequestWithBuyer(new Dictionary<string, object> { ["cardExpiration"] = DateTime.UtcNow.AddYears(1) }, ValidQuote());
        _orderRepositoryMock.UnitOfWork.SaveEntitiesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new OrderingDomainException("payment method rejected")));

        var handler = new CreateOrderCommandHandler(_mediator, _orderingIntegrationEventService, _orderRepositoryMock, _identityServiceMock, Substitute.For<ILogger<CreateOrderCommandHandler>>());

        await Assert.ThrowsExactlyAsync<OrderingDomainException>(() => handler.Handle(command, CancellationToken.None));
        await _orderingIntegrationEventService.DidNotReceive().AddAndSaveEventAsync(Arg.Any<IntegrationEvent>());
    }

    private static PriceQuote ValidQuote() => new(
        [new PricedLine(1, "cup", null, 1, BasePrice: 10m, UnitPrice: 10m, Discount: 0m, PromotionLabel: null)],
        Subtotal: 10m, DiscountTotal: 0m, ShippingFee: 0m, Total: 10m, SnapshotVersion: 0);

    [TestMethod]
    public async Task Handle_creates_order_with_quoted_prices_and_pricing_snapshot()
    {
        var quote = new PriceQuote(
            [
                new PricedLine(1, "cup", null, 2, BasePrice: 12.00m, UnitPrice: 10.00m, Discount: 3.00m, PromotionLabel: null),
                new PricedLine(2, "mug", null, 1, BasePrice: 5.00m, UnitPrice: 5.00m, Discount: 0m, PromotionLabel: null),
            ],
            Subtotal: 25.00m, DiscountTotal: 3.00m, ShippingFee: 4.99m, Total: 26.99m, SnapshotVersion: 7);
        var command = FakeOrderRequestWithBuyer(new Dictionary<string, object> { ["cardExpiration"] = DateTime.UtcNow.AddYears(1) }, quote);
        Order added = null;
        _orderRepositoryMock.Add(Arg.Do<Order>(o => added = o));
        _orderRepositoryMock.UnitOfWork.SaveEntitiesAsync(Arg.Any<CancellationToken>()).Returns(true);

        var handler = new CreateOrderCommandHandler(_mediator, _orderingIntegrationEventService, _orderRepositoryMock, _identityServiceMock, Substitute.For<ILogger<CreateOrderCommandHandler>>());
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.IsTrue(result);
        Assert.IsNotNull(added);
        Assert.AreEqual(4.99m, added.ShippingFee);
        Assert.AreEqual(7, added.PricingSnapshotVersion);
        Assert.AreEqual(26.99m, added.Total);
        var cup = added.OrderItems.Single(i => i.ProductId == 1);
        Assert.AreEqual(12.00m, cup.BasePrice);
        Assert.AreEqual(10.00m, cup.UnitPrice);
        Assert.AreEqual(3.00m, cup.Discount);
        Assert.AreEqual(2, cup.Units);
    }

    [TestMethod]
    public void Handle_throws_exception_when_no_buyerId()
    {
        //Assert
        Assert.ThrowsExactly<ArgumentNullException>(() => new Buyer(string.Empty, string.Empty));
    }

    private Buyer FakeBuyer()
    {
        return new Buyer(Guid.NewGuid().ToString(), "1");
    }

    private Order FakeOrder()
    {
        return new Order("1", "fakeName", new Address("street", "city", "state", "country", "zipcode"), 1, "12", "111", "fakeName", DateTime.UtcNow.AddYears(1));
    }

    private CreateOrderCommand FakeOrderRequestWithBuyer(Dictionary<string, object> args = null, PriceQuote quote = null)
    {
        return new CreateOrderCommand(
            quote ?? new PriceQuote([], 0m, 0m, 0m, 0m, 0),
            userId: args != null && args.ContainsKey("userId") ? (string)args["userId"] : null,
            userName: args != null && args.ContainsKey("userName") ? (string)args["userName"] : null,
            city: args != null && args.ContainsKey("city") ? (string)args["city"] : null,
            street: args != null && args.ContainsKey("street") ? (string)args["street"] : null,
            state: args != null && args.ContainsKey("state") ? (string)args["state"] : null,
            country: args != null && args.ContainsKey("country") ? (string)args["country"] : null,
            zipcode: args != null && args.ContainsKey("zipcode") ? (string)args["zipcode"] : null,
            cardNumber: args != null && args.ContainsKey("cardNumber") ? (string)args["cardNumber"] : "1234",
            cardExpiration: args != null && args.ContainsKey("cardExpiration") ? (DateTime)args["cardExpiration"] : DateTime.MinValue,
            cardSecurityNumber: args != null && args.ContainsKey("cardSecurityNumber") ? (string)args["cardSecurityNumber"] : "123",
            cardHolderName: args != null && args.ContainsKey("cardHolderName") ? (string)args["cardHolderName"] : "XXX",
            cardTypeId: args != null && args.ContainsKey("cardTypeId") ? (int)args["cardTypeId"] : 0);
    }
}
