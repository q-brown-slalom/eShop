namespace eShop.Ordering.API.Application.Commands;

using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;
using eShop.Ordering.API.Application.Rules;

// Regular CommandHandler
public class CreateOrderCommandHandler
    : IRequestHandler<CreateOrderCommand, bool>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IIdentityService _identityService;
    private readonly IMediator _mediator;
    private readonly IOrderingIntegrationEventService _orderingIntegrationEventService;
    private readonly ILogger<CreateOrderCommandHandler> _logger;
    private readonly IOrderFulfilmentRulesResolver _rulesResolver;

    // Using DI to inject infrastructure persistence Repositories
    public CreateOrderCommandHandler(IMediator mediator,
        IOrderingIntegrationEventService orderingIntegrationEventService,
        IOrderRepository orderRepository,
        IIdentityService identityService,
        ILogger<CreateOrderCommandHandler> logger,
        IOrderFulfilmentRulesResolver rulesResolver)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _orderingIntegrationEventService = orderingIntegrationEventService ?? throw new ArgumentNullException(nameof(orderingIntegrationEventService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rulesResolver = rulesResolver ?? throw new ArgumentNullException(nameof(rulesResolver));
    }

    public async Task<bool> Handle(CreateOrderCommand message, CancellationToken cancellationToken)
    {
        // Add/Update the Buyer AggregateRoot
        // DDD patterns comment: Add child entities and value-objects through the Order Aggregate-Root
        // methods and constructor so validations, invariants and business logic 
        // make sure that consistency is preserved across the whole aggregate
        var address = new Address(message.Street, message.City, message.State, message.Country, message.ZipCode);
        var order = new Order(message.UserId, message.UserName, address, message.CardTypeId, message.CardNumber, message.CardSecurityNumber, message.CardHolderName, message.CardExpiration);

        // The quote merges lines by product id, so each product appears once and AddOrderItem never has to merge a
        // duplicate (which would drop that line's promotion label and order discount share).
        foreach (var item in message.OrderItems)
        {
            order.AddOrderItem(item.ProductId, item.ProductName, item.UnitPrice, item.Discount, item.PictureUrl, item.Units, item.BasePrice, item.PromotionLabel, item.OrderDiscountShare);
        }

        // Fixed for the life of the order, so a later rules change never moves an existing order's grace period or policy.
        var rules = _rulesResolver.Resolve();
        order.SetFulfilmentRules(rules.GracePeriod, rules.ShortfallPolicy);
        order.ApplyPricing(message.ShippingFee, message.PricingSnapshotVersion);

        _orderRepository.Add(order);

        using (_logger.BeginScope(new List<KeyValuePair<string, object>>
        {
            new("order.id", order.Id),
            new("rules.snapshot.version", message.PricingSnapshotVersion),
            new("rules.grace_period_minutes", rules.GracePeriod.TotalMinutes),
            new("rules.shortfall_policy", rules.ShortfallPolicy.ToString())
        }))
        {
            _logger.LogInformation("Creating order with {LineCount} lines", order.OrderItems.Count);

            if (!await _orderRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken))
            {
                return false;
            }
        }

        // The basket is cleared only for an order that was built and saved. The event joins the same transaction,
        // so a failure anywhere in the command leaves neither the order nor the event behind.
        var orderStartedIntegrationEvent = new OrderStartedIntegrationEvent(message.UserId);
        await _orderingIntegrationEventService.AddAndSaveEventAsync(orderStartedIntegrationEvent);

        return true;
    }
}


// Use for Idempotency in Command process
public class CreateOrderIdentifiedCommandHandler : IdentifiedCommandHandler<CreateOrderCommand, bool>
{
    public CreateOrderIdentifiedCommandHandler(
        IMediator mediator,
        IRequestManager requestManager,
        ILogger<IdentifiedCommandHandler<CreateOrderCommand, bool>> logger)
        : base(mediator, requestManager, logger)
    {
    }

    protected override bool CreateResultForDuplicateRequest()
    {
        return true; // Ignore duplicate requests for creating order.
    }
}
