using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using eShop.BusinessRules.Client.Snapshot;
using eShop.Ordering.API.Application.Pricing;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace eShop.Ordering.FunctionalTests;

public sealed class OrderingApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly IHost _app;

    public IResourceBuilder<PostgresServerResource> Postgres { get; private set; }
    public IResourceBuilder<PostgresServerResource> IdentityDB { get; private set; }
    public IResourceBuilder<ProjectResource> IdentityApi { get; private set; }

    /// <summary>
    /// Stands in for BusinessRules.API. It fails every fetch (so pricing falls back to defaults) until a test calls
    /// <see cref="PublishSnapshotAsync"/>.
    /// </summary>
    public TestRulesSnapshotSource RulesSnapshotSource { get; } = new();

    /// <summary>Stands in for Catalog.API. Tests that override a price must reset it.</summary>
    public FakeCatalogPriceSource CatalogPriceSource { get; } = new();

    private string _postgresConnectionString;

    public OrderingApiFixture()
    {
        // IntegrationEventLogService resolves integration event types from the entry assembly. In production that is
        // Ordering.API; under the test runner it is this test assembly, so publishing after a committed create-order
        // failed with a 500. Point the entry assembly at Ordering.API, as in production.
        Assembly.SetEntryAssembly(typeof(Program).Assembly);

        var options = new DistributedApplicationOptions { AssemblyName = typeof(OrderingApiFixture).Assembly.FullName, DisableDashboard = true };
        var appBuilder = DistributedApplication.CreateBuilder(options);
        Postgres = appBuilder.AddPostgres("OrderingDB");
        IdentityDB = appBuilder.AddPostgres("IdentityDB");
        IdentityApi = appBuilder.AddProject<Projects.Identity_API>("identity-api").WithReference(IdentityDB);
        _app = appBuilder.Build();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string>
            {
                { $"ConnectionStrings:{Postgres.Resource.Name}", _postgresConnectionString },
                { "Identity:Url", IdentityApi.GetEndpoint("http").Url }
            });
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter>(new AutoAuthorizeStartupFilter());

            services.RemoveAll<ICatalogPriceSource>();
            services.AddSingleton<ICatalogPriceSource>(CatalogPriceSource);

            services.RemoveAll<IRulesSnapshotSource>();
            services.AddSingleton<IRulesSnapshotSource>(RulesSnapshotSource);
        });
        return base.CreateHost(builder);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _app.StopAsync();
        if (_app is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _app.Dispose();
        }
    }

    public async ValueTask InitializeAsync()
    {
        await _app.StartAsync();
        _postgresConnectionString = await Postgres.Resource.GetConnectionStringAsync();
    }

    /// <summary>
    /// Publishes a snapshot with the next version that contains only <paramref name="rules"/> (it replaces anything
    /// published earlier), then makes the host's cache pick it up.
    /// </summary>
    public async Task PublishSnapshotAsync(params SnapshotRuleVersion[] rules)
    {
        var cache = Services.GetRequiredService<RulesSnapshotCache>();
        var version = Math.Max(RulesSnapshotSource.Snapshot?.Version ?? 0, cache.Current.Version) + 1;
        RulesSnapshotSource.Snapshot = new RulesSnapshot(version, DateTimeOffset.UtcNow, rules);

        Assert.True(await cache.RefreshAsync(TestContext.Current.CancellationToken));
        Assert.Equal(version, cache.Current.Version);
    }

    private class AutoAuthorizeStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return builder =>
            {
                builder.UseMiddleware<AutoAuthorizeMiddleware>();
                next(builder);
            };
        }
    }
}
