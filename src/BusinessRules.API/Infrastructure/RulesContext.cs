namespace eShop.BusinessRules.API.Infrastructure;

/// <remarks>
/// Add migrations using the following command inside the 'BusinessRules.API' project directory:
///
/// dotnet ef migrations add --context RulesContext [migration-name]
/// </remarks>
public class RulesContext(DbContextOptions<RulesContext> options) : DbContext(options)
{
    public const string DefaultSchema = "rules";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(DefaultSchema);

        // Add the outbox table to this context
        builder.UseIntegrationEventLogs();
    }
}
