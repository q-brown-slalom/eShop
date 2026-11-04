using System.Text.Json.Serialization;
using eShop.BusinessRules.Client;
using Microsoft.Extensions.DependencyInjection.Extensions;

internal static class Extensions
{
    private const string OrderingUsageScope = "ordering.usage";

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
        services.AddTransient<IRulesIntegrationEventService, RulesIntegrationEventService>();
        services.AddScoped<RulePublishingService>();
        services.AddSingleton<BusinessRulesMetrics>();
        services.AddScoped<IPublishGuard, V1ClientsPublishGuard>();
        services.AddHttpClient<IOrderingUsageClient, OrderingUsageClient>(client => client.BaseAddress = new Uri("https+http://ordering-api"))
            .AddApiVersion(1.0)
            .AddServiceToken(OrderingUsageScope);

        // Rule types are registered in code with AddRuleType<T>(); phase 1 registers none (D1).
        services.TryAddSingleton<RuleTypeRegistry>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssemblyContaining<CreateDraftRequestValidator>();

        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        builder.AddRabbitMqEventBus("eventbus");
    }
}
