using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dotnet;
using eShop.AppHost;

namespace eShop.AppHost.UnitTests;

[TestClass]
public class BusinessRulesResourceTests
{
    [TestMethod]
    public void BusinessRulesApiReferencesRulesDbAndEventBus()
    {
        var (builder, _) = CreateBusinessRulesModel();

        var rulesDb = builder.Resources.OfType<PostgresDatabaseResource>().Single(r => r.Name == "rulesdb");
        Assert.AreEqual("postgres", rulesDb.Parent.Name);

        var businessRulesApi = GetProject(builder, "businessrules-api");
        CollectionAssert.IsSubsetOf(
            new[] { "rulesdb", "eventbus", "ordering-api" },
            ReferencedResourceNames(businessRulesApi));
        CollectionAssert.IsSubsetOf(
            new[] { "rulesdb", "eventbus" },
            WaitedResourceNames(businessRulesApi));
        CollectionAssert.DoesNotContain(WaitedResourceNames(businessRulesApi), "ordering-api");
    }

    [TestMethod]
    public void OrderingApiDoesNotWaitForBusinessRulesApi()
    {
        var (builder, orderingApi) = CreateBusinessRulesModel();

        CollectionAssert.Contains(ReferencedResourceNames(orderingApi.Resource), "businessrules-api");
        CollectionAssert.DoesNotContain(WaitedResourceNames(orderingApi.Resource), "businessrules-api");
        Assert.IsTrue(builder.Resources.OfType<ParameterResource>().Any(p => p.Name == "businessrules-reader-secret" && p.Secret));
        Assert.IsTrue(builder.Resources.OfType<ParameterResource>().Any(p => p.Name == "businessrules-api-secret" && p.Secret));
    }

    private static (IDistributedApplicationBuilder Builder, IResourceBuilder<DotnetProjectResource> OrderingApi) CreateBusinessRulesModel()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            AssemblyName = typeof(BusinessRulesResourceTests).Assembly.FullName,
            DisableDashboard = true,
            ProjectDirectory = Path.Combine(FindRepositoryRoot(), "src", "eShop.AppHost")
        });

        var rabbitMq = builder.AddRabbitMQ("eventbus");
        var postgres = builder.AddPostgres("postgres");
        var identityApi = builder.AddDotnetProject("identity-api", "../Identity.API");
        var orderingApi = builder.AddDotnetProject("ordering-api", "../Ordering.API");

        builder.AddBusinessRules(postgres, rabbitMq, orderingApi, identityApi, identityApi.GetEndpoint("http"));

        return (builder, orderingApi);
    }

    private static DotnetProjectResource GetProject(IDistributedApplicationBuilder builder, string name) =>
        builder.Resources.OfType<DotnetProjectResource>().Single(r => r.Name == name);

    private static string[] ReferencedResourceNames(IResource resource) =>
        [.. resource.Annotations.OfType<ResourceRelationshipAnnotation>()
            .Where(a => a.Type == "Reference")
            .Select(a => a.Resource.Name)];

    private static string[] WaitedResourceNames(IResource resource) =>
        [.. resource.Annotations.OfType<WaitAnnotation>().Select(a => a.Resource.Name)];

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
