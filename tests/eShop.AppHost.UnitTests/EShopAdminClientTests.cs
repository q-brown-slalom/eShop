using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dotnet;
using eShop.AppHost;

namespace eShop.AppHost.UnitTests;

[TestClass]
public class EShopAdminClientTests
{
    [TestMethod]
    public async Task IdentityApiReceivesEShopAdminClientUrl()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            AssemblyName = typeof(EShopAdminClientTests).Assembly.FullName,
            DisableDashboard = true,
            ProjectDirectory = Path.Combine(FindRepositoryRoot(), "src", "eShop.AppHost")
        });
        var identityApi = builder.AddDotnetProject("identity-api", "../Identity.API");

        var parameter = builder.AddEShopAdminClient(identityApi);

        Assert.AreEqual("eshopadmin-url", parameter.Resource.Name);
        Assert.IsFalse(parameter.Resource.Secret);
        Assert.AreEqual(1, builder.Resources.OfType<ParameterResource>().Count(p => p.Name == "eshopadmin-url"));

        var context = new EnvironmentCallbackContext(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run));
        foreach (var callback in identityApi.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        Assert.IsTrue(context.EnvironmentVariables.TryGetValue("EShopAdminClient", out var value));
        Assert.AreSame(parameter.Resource, value);
    }

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
