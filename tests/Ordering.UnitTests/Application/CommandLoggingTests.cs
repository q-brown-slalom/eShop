using System.Text.Json;
using eShop.BusinessRules.Client.Pricing;
using eShop.EventBus.Abstractions;
using eShop.IntegrationEventLogEF.Services;
using eShop.Ordering.API.Application.Behaviors;
using eShop.Ordering.API.Application.IntegrationEvents;
using eShop.Ordering.API.Application.IntegrationEvents.Events;
using eShop.Ordering.API.Application.Validations;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;
using eShop.Ordering.Infrastructure;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace eShop.Ordering.UnitTests.Application;

/// <summary>
/// The create-order pipeline must never log the card security number, card holder, address, or user name (security.md).
/// Each test captures the formatted message, every structured value (serialized, as a destructuring sink would), and scopes.
/// </summary>
[TestClass]
public class CommandLoggingTests
{
    private const string SecurityNumber = "987";
    private const string InvalidSecurityNumber = "98765";
    private const string CardHolder = "Jane Q Cardholder";
    private const string Street = "1 Secret Lane";
    private const string City = "Hiddenville";
    private const string ZipCode = "ZIP-5150";
    private const string UserName = "jane.secret@example.com";

    private static readonly string[] SensitiveValues = [SecurityNumber, InvalidSecurityNumber, CardHolder, Street, City, ZipCode, UserName];

    [TestMethod]
    public async Task LoggingBehaviorDoesNotLogCreateOrderCommandPii()
    {
        var logs = new CapturingLogger<LoggingBehavior<CreateOrderCommand, bool>>();
        var behavior = new LoggingBehavior<CreateOrderCommand, bool>(logs);

        await behavior.Handle(Command(), _ => Task.FromResult(true), CancellationToken.None);

        logs.AssertLogged("Handling command CreateOrderCommand");
        logs.AssertContainsNone(SensitiveValues);
    }

    [TestMethod]
    public async Task ValidatorBehaviorDoesNotLogCreateOrderCommandPiiOrAttemptedValues()
    {
        var logs = new CapturingLogger<ValidatorBehavior<CreateOrderCommand, bool>>();
        var validator = new CreateOrderCommandValidator(Substitute.For<ILogger<CreateOrderCommandValidator>>());
        var behavior = new ValidatorBehavior<CreateOrderCommand, bool>([validator], logs);

        await Assert.ThrowsExactlyAsync<OrderingDomainException>(
            () => behavior.Handle(Command(InvalidSecurityNumber), _ => Task.FromResult(true), CancellationToken.None));

        logs.AssertLogged("CardSecurityNumber");
        logs.AssertContainsNone(SensitiveValues);
    }

    [TestMethod]
    public async Task IdentifiedCommandHandlerDoesNotLogCreateOrderCommandPii()
    {
        var logs = new CapturingLogger<IdentifiedCommandHandler<CreateOrderCommand, bool>>();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<IRequest<bool>>(), Arg.Any<CancellationToken>()).Returns(true);
        var requestId = Guid.NewGuid();
        var handler = new CreateOrderIdentifiedCommandHandler(mediator, Substitute.For<IRequestManager>(), logs);

        await handler.Handle(new IdentifiedCommand<CreateOrderCommand, bool>(Command(), requestId), CancellationToken.None);

        logs.AssertLogged(requestId.ToString());
        logs.AssertContainsNone(SensitiveValues);
    }

    [TestMethod]
    public async Task TransactionBehaviorDoesNotLogCreateOrderCommandPiiOnFailure()
    {
        var logs = new CapturingLogger<TransactionBehavior<CreateOrderCommand, bool>>();
        // Nothing listens on port 1, so opening the transaction fails and the behavior logs the error.
        var options = new DbContextOptionsBuilder<OrderingContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2")
            .Options;
        await using var context = new OrderingContext(options, Substitute.For<IMediator>());
        var behavior = new TransactionBehavior<CreateOrderCommand, bool>(context, Substitute.For<IOrderingIntegrationEventService>(), logs);

        await Assert.ThrowsAsync<Exception>(() => behavior.Handle(Command(), _ => Task.FromResult(true), CancellationToken.None));

        logs.AssertLogged("Error Handling transaction for CreateOrderCommand");
        logs.AssertContainsNone(SensitiveValues);
    }

    [TestMethod]
    public async Task IntegrationEventServiceDoesNotLogBuyerName()
    {
        var logs = new CapturingLogger<OrderingIntegrationEventService>();
        var options = new DbContextOptionsBuilder<OrderingContext>().UseNpgsql("Host=127.0.0.1;Port=1").Options;
        await using var context = new OrderingContext(options, Substitute.For<IMediator>());
        var service = new OrderingIntegrationEventService(Substitute.For<IEventBus>(), context, Substitute.For<IIntegrationEventLogService>(), logs);

        await service.AddAndSaveEventAsync(new OrderStatusChangedToSubmittedIntegrationEvent(1, OrderStatus.Submitted, UserName, "buyer-1"));

        logs.AssertLogged(nameof(OrderStatusChangedToSubmittedIntegrationEvent));
        logs.AssertContainsNone(SensitiveValues);
    }

    private static CreateOrderCommand Command(string securityNumber = SecurityNumber) => new(
        new PriceQuote([new PricedLine(1, "cup", null, 1, 10m, 10m, 0m, null)], 10m, 0m, 0m, 10m, 3),
        userId: "user-1",
        userName: UserName,
        city: City,
        street: Street,
        state: "WA",
        country: "U.S.",
        zipcode: ZipCode,
        cardNumber: "XXXXXXXXXXXX4242",
        cardHolderName: CardHolder,
        cardExpiration: DateTime.UtcNow.AddYears(1),
        cardSecurityNumber: securityNumber,
        cardTypeId: 1);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _entries = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            Record(state);
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add(formatter(state, exception));
                if (exception is not null)
                {
                    _entries.Add(exception.ToString());
                }
            }

            Record(state);
        }

        public void AssertLogged(string expected)
        {
            Assert.IsTrue(_entries.Any(e => e.Contains(expected, StringComparison.Ordinal)),
                $"Expected a log entry containing '{expected}'. Logged:{Environment.NewLine}{string.Join(Environment.NewLine, _entries)}");
        }

        public void AssertContainsNone(IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                var leak = _entries.FirstOrDefault(e => e.Contains(value, StringComparison.Ordinal));
                Assert.IsNull(leak, $"'{value}' was logged in: {leak}");
            }
        }

        private void Record(object state)
        {
            lock (_entries)
            {
                _entries.Add(state?.ToString() ?? string.Empty);
                if (state is IEnumerable<KeyValuePair<string, object>> values)
                {
                    foreach (var (_, value) in values)
                    {
                        _entries.Add(Serialize(value));
                    }
                }
            }
        }

        private static string Serialize(object value)
        {
            if (value is null)
            {
                return string.Empty;
            }

            try
            {
                return $"{value} {JsonSerializer.Serialize(value, value.GetType())}";
            }
            catch (Exception)
            {
                return value.ToString();
            }
        }
    }
}
