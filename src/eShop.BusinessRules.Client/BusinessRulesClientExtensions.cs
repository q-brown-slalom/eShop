using eShop.BusinessRules.Client.Auth;
using eShop.BusinessRules.Client.Pricing;
using eShop.BusinessRules.Client.Rules;
using eShop.BusinessRules.Client.Snapshot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace eShop.BusinessRules.Client;

public static class BusinessRulesClientExtensions
{
    public const string ServiceScope = "businessrules";

    private const string BusinessRulesApiBaseAddress = "https+http://businessrules-api";

    /// <summary>
    /// Registers the rules snapshot cache, its background refresh, the pricing calculator and the
    /// authenticated snapshot client. Consumers subscribe to
    /// <see cref="IntegrationEvents.BusinessRulesSnapshotPublishedIntegrationEvent"/> on their own event bus.
    /// </summary>
    public static IHostApplicationBuilder AddBusinessRulesClient(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<RuleTypeRegistry>();
        services.TryAddSingleton<PricingCalculator>();

        services.AddOptions<BusinessRulesClientOptions>().BindConfiguration(BusinessRulesClientOptions.SectionName);
        services.TryAddSingleton<RulesSnapshotCache>();
        services.TryAddSingleton<BusinessRulesClientMetrics>();
        services.AddHostedService<RulesSnapshotRefreshService>();

        // The cache is a singleton that holds this typed client for the app's lifetime, so connection
        // recycling comes from the socket handler rather than handler rotation.
        services.AddHttpClient<IRulesSnapshotSource, HttpRulesSnapshotSource>(client => client.BaseAddress = new Uri(BusinessRulesApiBaseAddress))
            .UseSocketsHttpHandler((handler, _) => handler.PooledConnectionLifetime = TimeSpan.FromMinutes(2))
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .AddServiceToken(ServiceScope);

        return builder;
    }

    /// <summary>Authenticates requests with an OAuth client-credentials token for <paramref name="scope"/> (plan decision D2).</summary>
    public static IHttpClientBuilder AddServiceToken(this IHttpClientBuilder builder, string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(ServiceTokenProvider.HttpClientName);
        services.AddOptions<ServiceClientOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.ClientId = configuration["ServiceClient:ClientId"] ?? string.Empty;
                options.ClientSecret = configuration["ServiceClient:ClientSecret"] ?? string.Empty;
                options.IdentityUrl = configuration["Identity:Url"] ?? string.Empty;
            });
        services.TryAddKeyedSingleton(scope, static (sp, key) => new ServiceTokenProvider(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptions<ServiceClientOptions>>(),
            (string)key!,
            sp.GetRequiredService<TimeProvider>()));

        return builder.AddHttpMessageHandler(sp => new ServiceTokenHandler(sp.GetRequiredKeyedService<ServiceTokenProvider>(scope)));
    }
}
