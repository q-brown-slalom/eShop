using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dotnet;
using eShop.AppHost;

namespace eShop.AppHost.UnitTests;

[TestClass]
public class MobileBffRoutesTests
{
    private const string RoutesPrefix = "REVERSEPROXY__ROUTES__";
    private const string ClustersPrefix = "REVERSEPROXY__CLUSTERS__";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task MobileBffRoutesPricingQuoteToOrdering()
    {
        var environment = await GetMobileBffEnvironmentAsync();

        var route = GetRoutes(environment).Single(r => r.Path == "/api/pricing/quote");

        Assert.AreEqual("cluster_ordering-api", route.ClusterId);
        CollectionAssert.AreEquivalent(new[] { "1.0", "1" }, route.ApiVersions);
        Assert.AreEqual("http://ordering-api", environment[$"{ClustersPrefix}cluster_ordering-api__DESTINATIONS__destination1__ADDRESS"]);
    }

    [TestMethod]
    public async Task MobileBffOrdersRouteAcceptsVersion2()
    {
        var environment = await GetMobileBffEnvironmentAsync();

        var route = GetRoutes(environment).Single(r => r.Path == "/api/orders/{*any}");

        Assert.AreEqual("cluster_ordering-api", route.ClusterId);
        CollectionAssert.AreEquivalent(new[] { "1.0", "1", "2.0" }, route.ApiVersions);
    }

    [TestMethod]
    public async Task MobileBffHasNoBusinessRulesRoute()
    {
        var environment = await GetMobileBffEnvironmentAsync();

        var businessRulesKeys = environment
            .Where(kv => kv.Key.Contains("businessrules", StringComparison.OrdinalIgnoreCase)
                || (kv.Value as string)?.Contains("businessrules", StringComparison.OrdinalIgnoreCase) == true)
            .Select(kv => kv.Key)
            .ToArray();

        Assert.IsEmpty(businessRulesKeys);
        Assert.IsFalse(GetRoutes(environment).Any(r => r.ClusterId.Contains("businessrules", StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<Dictionary<string, object>> GetMobileBffEnvironmentAsync()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            AssemblyName = typeof(MobileBffRoutesTests).Assembly.FullName,
            DisableDashboard = true,
            ProjectDirectory = Path.Combine(FindRepositoryRoot(), "src", "eShop.AppHost")
        });

        var rabbitMq = builder.AddRabbitMQ("eventbus");
        var postgres = builder.AddPostgres("postgres");
        var identityApi = builder.AddDotnetProject("identity-api", "../Identity.API");
        var catalogApi = builder.AddDotnetProject("catalog-api", "../Catalog.API");
        var orderingApi = builder.AddDotnetProject("ordering-api", "../Ordering.API");
        builder.AddBusinessRules(postgres, rabbitMq, orderingApi, identityApi, identityApi.GetEndpoint("http"));
        var mobileBff = builder.AddYarp("mobile-bff")
            .ConfigureMobileBffRoutes(catalogApi, orderingApi, identityApi);

        // YARP's environment callbacks resolve services from the execution context, which requires a built app.
        using var app = builder.Build();
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, mobileBff.Resource, cancellationToken: TestContext.CancellationToken);
        foreach (var annotation in mobileBff.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables;
    }

    private static IReadOnlyList<MobileBffRoute> GetRoutes(Dictionary<string, object> environment) =>
        [.. environment.Keys
            .Where(key => key.StartsWith(RoutesPrefix, StringComparison.Ordinal))
            .Select(key => key[RoutesPrefix.Length..].Split("__")[0])
            .Distinct()
            .Select(routeId =>
            {
                var routeKey = RoutesPrefix + routeId + "__";
                var apiVersions = environment
                    .Where(kv => kv.Key.StartsWith(routeKey + "MATCH__QUERYPARAMETERS__0__VALUES__", StringComparison.Ordinal))
                    .Select(kv => (string)kv.Value)
                    .ToArray();
                return new MobileBffRoute(
                    (string)environment[routeKey + "MATCH__PATH"],
                    (string)environment[routeKey + "CLUSTERID"],
                    apiVersions);
            })];

    private sealed record MobileBffRoute(string Path, string ClusterId, string[] ApiVersions);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "eShop.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the eShop repository root.");
    }
}
