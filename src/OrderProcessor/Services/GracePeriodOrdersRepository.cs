using Npgsql;

namespace eShop.OrderProcessor.Services;

public interface IGracePeriodOrdersRepository
{
    ValueTask<List<int>> GetConfirmedGracePeriodOrdersAsync(CancellationToken cancellationToken);
}

internal sealed class GracePeriodOrdersRepository(
    NpgsqlDataSource dataSource,
    ILogger<GracePeriodOrdersRepository> logger) : IGracePeriodOrdersRepository
{
    public async ValueTask<List<int>> GetConfirmedGracePeriodOrdersAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = dataSource.CreateConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "Id"
                FROM ordering.orders
                WHERE "GracePeriodEndsAt" <= CURRENT_TIMESTAMP AND "OrderStatus" = 'Submitted'
                """;

            List<int> ids = [];
            await connection.OpenAsync(cancellationToken);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetInt32(0));
            }

            return ids;
        }
        catch (NpgsqlException exception)
        {
            logger.LogError(exception, "Error querying confirmed grace period orders");
            return [];
        }
    }
}
