namespace eShop.Ordering.API.Application.Commands;

/// <summary>
/// Provides a base implementation for handling duplicate request and ensuring idempotent updates, in the cases where
/// a requestid sent by client is used to detect duplicate requests.
/// </summary>
/// <typeparam name="T">Type of the command handler that performs the operation if request is not duplicated</typeparam>
/// <typeparam name="R">Return value of the inner command handler</typeparam>
public abstract class IdentifiedCommandHandler<T, R> : IRequestHandler<IdentifiedCommand<T, R>, R>
    where T : IRequest<R>
{
    private readonly IMediator _mediator;
    private readonly IRequestManager _requestManager;
    private readonly ILogger<IdentifiedCommandHandler<T, R>> _logger;

    public IdentifiedCommandHandler(
        IMediator mediator,
        IRequestManager requestManager,
        ILogger<IdentifiedCommandHandler<T, R>> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _mediator = mediator;
        _requestManager = requestManager;
        _logger = logger;
    }

    /// <summary>
    /// Creates the result value to return if a previous request was found
    /// </summary>
    /// <returns></returns>
    protected abstract R CreateResultForDuplicateRequest();

    /// <summary>
    /// This method handles the command. It just ensures that no other request exists with the same ID, and if this is the case
    /// just enqueues the original inner command.
    /// </summary>
    /// <param name="message">IdentifiedCommand which contains both original command & request ID</param>
    /// <returns>Return value of inner command or default value if request same ID was found</returns>
    public async Task<R> Handle(IdentifiedCommand<T, R> message, CancellationToken cancellationToken)
    {
        var alreadyExists = await _requestManager.ExistAsync(message.Id);
        if (alreadyExists)
        {
            return CreateResultForDuplicateRequest();
        }
        else
        {
            // Failures propagate so the surrounding TransactionBehavior rolls back everything the command wrote.
            var command = message.Command;
            var commandName = command.GetGenericTypeName();
            var idProperty = string.Empty;
            var commandId = string.Empty;

            // Only identifiers are logged, never the command: CreateOrderCommand carries card and address data.
            switch (command)
            {
                case CreateOrderCommand:
                    idProperty = "RequestId";
                    commandId = $"{message.Id}";
                    break;

                case CancelOrderCommand cancelOrderCommand:
                    idProperty = nameof(cancelOrderCommand.OrderNumber);
                    commandId = $"{cancelOrderCommand.OrderNumber}";
                    break;

                case ShipOrderCommand shipOrderCommand:
                    idProperty = nameof(shipOrderCommand.OrderNumber);
                    commandId = $"{shipOrderCommand.OrderNumber}";
                    break;

                default:
                    idProperty = "Id?";
                    commandId = "n/a";
                    break;
            }

            _logger.LogInformation(
                "Sending command: {CommandName} - {IdProperty}: {CommandId}",
                commandName,
                idProperty,
                commandId);

            // Send the embedded business command to mediator so it runs its related CommandHandler 
            var result = await _mediator.Send(command, cancellationToken);

            _logger.LogInformation(
                "Command result: {@Result} - {CommandName} - {IdProperty}: {CommandId}",
                result,
                commandName,
                idProperty,
                commandId);

            // The request is recorded only after the command succeeds (a default result such as false means it failed),
            // so a retry with the same request id runs the command again. A failure here, such as a concurrent duplicate,
            // propagates and the surrounding transaction rolls back the command's changes.
            if (!EqualityComparer<R>.Default.Equals(result, default))
            {
                await _requestManager.CreateRequestForCommandAsync<T>(message.Id);
            }

            return result;
        }
    }
}
