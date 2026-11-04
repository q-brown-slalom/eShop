using eShop.BusinessRules.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace eShop.BusinessRules.UnitTests;

[TestClass]
public sealed class BusinessRulesClientExtensionsTests
{
    [TestMethod]
    public void AddBusinessRulesClientRegistersResolvableServices()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.AddBusinessRulesClient();

        using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.AreEqual(0L, provider.GetRequiredService<RulesSnapshotCache>().Current.Version);
        Assert.IsInstanceOfType<HttpRulesSnapshotSource>(provider.GetRequiredService<IRulesSnapshotSource>());
        Assert.IsNotNull(provider.GetRequiredService<PricingCalculator>());
        Assert.IsNotNull(provider.GetRequiredService<RuleTypeRegistry>());
        Assert.ContainsSingle(provider.GetServices<IHostedService>().OfType<RulesSnapshotRefreshService>());
    }
}
