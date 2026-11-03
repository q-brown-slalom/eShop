internal static class Extensions
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;

        builder.AddDefaultAuthentication();
        services.AddAuthorizationBuilder().AddRulesPolicies();

        // Registers the DbContext plus its health check, retries and telemetry
        builder.AddNpgsqlDbContext<RulesContext>("rulesdb");

        // REVIEW: This is done for development ease but shouldn't be here in production
        services.AddMigration<RulesContext, RulesContextSeed>();

        services.AddTransient<IIntegrationEventLogService, IntegrationEventLogService<RulesContext>>();

        builder.AddRabbitMqEventBus("eventbus");
    }
}
