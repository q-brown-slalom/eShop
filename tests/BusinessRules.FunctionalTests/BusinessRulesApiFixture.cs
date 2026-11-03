using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using eShop.BusinessRules.UnitTests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace eShop.BusinessRules.FunctionalTests;

public sealed class BusinessRulesApiFixture : WebApplicationFactory<Program>
{
    private readonly IHost _app;
    private string? _postgresConnectionString;

    public IResourceBuilder<PostgresServerResource> Postgres { get; }

    public BusinessRulesApiFixture()
    {
        var options = new DistributedApplicationOptions { AssemblyName = typeof(BusinessRulesApiFixture).Assembly.FullName, DisableDashboard = true };
        var appBuilder = DistributedApplication.CreateBuilder(options);
        Postgres = appBuilder.AddPostgres("RulesDB");
        _app = appBuilder.Build();
    }

    public async Task InitializeAsync()
    {
        await _app.StartAsync();
        _postgresConnectionString = await Postgres.Resource.GetConnectionStringAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { $"ConnectionStrings:{Postgres.Resource.Name}", _postgresConnectionString },
            });
        });
        builder.ConfigureServices(services =>
        {
            services.AddRuleType<TestRuleType>();
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
        return base.CreateHost(builder);
    }

    public override async ValueTask DisposeAsync()
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
}

[TestClass]
public static class BusinessRulesApiAssembly
{
    public static BusinessRulesApiFixture Fixture { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext _)
    {
        Fixture = new BusinessRulesApiFixture();
        await Fixture.InitializeAsync();
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        if (Fixture is not null)
        {
            await Fixture.DisposeAsync();
        }
    }
}
