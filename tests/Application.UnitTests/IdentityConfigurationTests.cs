using System.Security.Claims;
using Duende.IdentityServer.Models;
using eShop.Identity.API.Configuration;
using eShop.Identity.API.Models;
using eShop.Identity.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace eShop.Application.UnitTests;

[TestClass]
public class IdentityConfigurationTests
{
    [TestMethod]
    public void ApiResourcesAndScopesStayAligned()
    {
        var resourceScopes = Config.GetApis()
            .SelectMany(resource => resource.Scopes.Count > 0 ? resource.Scopes : [resource.Name])
            .Order()
            .ToArray();
        var scopes = Config.GetApiScopes().Select(scope => scope.Name).Order().ToArray();

        CollectionAssert.AreEqual(resourceScopes, scopes);
        CollectionAssert.AreEquivalent(new[] { "basket", "businessrules", "businessrules.admin", "ordering.usage", "orders", "webhooks" }, scopes);
    }

    [TestMethod]
    public void ClientsUseConfiguredCallbackUrlsAndExpectedScopes()
    {
        var values = new Dictionary<string, string?>
        {
            ["MauiCallback"] = "maui://callback",
            ["WebAppClient"] = "https://webapp.test",
            ["WebhooksWebClient"] = "https://webhooks-client.test",
            ["BasketApiClient"] = "https://basket.test",
            ["OrderingApiClient"] = "https://ordering.test",
            ["WebhooksApiClient"] = "https://webhooks.test"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var clients = Config.GetClients(configuration).ToDictionary(client => client.ClientId);

        CollectionAssert.Contains(clients["webapp"].RedirectUris.ToList(), "https://webapp.test/signin-oidc");
        CollectionAssert.Contains(clients["webhooksclient"].AllowedScopes.ToList(), "webhooks");
        CollectionAssert.Contains(clients["maui"].AllowedScopes.ToList(), "basket");
        CollectionAssert.Contains(clients["maui"].AllowedScopes.ToList(), "orders");
    }

    [TestMethod]
    public void BusinessRulesScopesAreDefined()
    {
        var scopes = Config.GetApiScopes().ToDictionary(scope => scope.Name);

        Assert.IsTrue(scopes.ContainsKey("businessrules"));
        Assert.IsTrue(scopes.ContainsKey("businessrules.admin"));
        Assert.IsTrue(scopes.ContainsKey("ordering.usage"));
        CollectionAssert.Contains(scopes["businessrules.admin"].UserClaims.ToList(), "role");
    }

    [TestMethod]
    public void ServiceClientsUseClientCredentialsWithLeastPrivilege()
    {
        var clients = Config.GetClients(BuildConfiguration()).ToDictionary(client => client.ClientId);

        var reader = clients["businessrules-reader"];
        var api = clients["businessrules-api"];

        CollectionAssert.AreEqual(new[] { "businessrules" }, reader.AllowedScopes.ToArray());
        CollectionAssert.AreEqual(new[] { "ordering.usage" }, api.AllowedScopes.ToArray());
        CollectionAssert.AreEquivalent(GrantTypes.ClientCredentials.ToArray(), reader.AllowedGrantTypes.ToArray());
        CollectionAssert.AreEquivalent(GrantTypes.ClientCredentials.ToArray(), api.AllowedGrantTypes.ToArray());
        Assert.AreEqual("reader-secret".Sha256(), reader.ClientSecrets.Single().Value);
        Assert.AreEqual("api-secret".Sha256(), api.ClientSecrets.Single().Value);
    }

    [TestMethod]
    public void BusinessRulesSwaggerUiClientUsesAdminScope()
    {
        var clients = Config.GetClients(BuildConfiguration()).ToDictionary(client => client.ClientId);

        var swagger = clients["businessrulesswaggerui"];

        CollectionAssert.AreEqual(new[] { "businessrules.admin" }, swagger.AllowedScopes.ToArray());
        CollectionAssert.Contains(swagger.RedirectUris.ToList(), "https://businessrules.test/swagger/oauth2-redirect.html");
    }

    [TestMethod]
    public void EShopAdminClientRequiresPkceAndAdminScopeOnly()
    {
        var clients = Config.GetClients(BuildConfiguration()).ToDictionary(client => client.ClientId);

        var admin = clients["eshopadmin"];

        Assert.IsTrue(admin.RequirePkce);
        Assert.IsFalse(admin.RequireClientSecret);
        CollectionAssert.AreEquivalent(GrantTypes.Code.ToArray(), admin.AllowedGrantTypes.ToArray());
        CollectionAssert.Contains(admin.RedirectUris.ToList(), "https://admin.test/signin-oidc");
        CollectionAssert.AreEquivalent(new[] { "openid", "profile", "businessrules.admin" }, admin.AllowedScopes.ToArray());
        CollectionAssert.DoesNotContain(admin.AllowedScopes.ToList(), "orders");
        CollectionAssert.DoesNotContain(admin.AllowedScopes.ToList(), "basket");
    }

    [TestMethod]
    public void EShopAdminClientOmittedWhenNotConfigured()
    {
        var configuration = BuildConfiguration(includeAdmin: false);

        var clients = Config.GetClients(configuration);

        Assert.IsFalse(clients.Any(client => client.ClientId == "eshopadmin"));
    }

    [TestMethod]
    public async Task ProfileServiceAddsRoleClaimsWhenRequested()
    {
        var (service, context) = CreateProfileRequest(["role"]);

        await service.GetProfileDataAsync(context);

        Assert.IsTrue(context.IssuedClaims.Any(claim => claim.Type == "role" && claim.Value == "RulesPublisher"));
    }

    [TestMethod]
    public async Task ProfileServiceOmitsRoleClaimsWhenNotRequested()
    {
        var (service, context) = CreateProfileRequest(["name"]);

        await service.GetProfileDataAsync(context);

        Assert.IsFalse(context.IssuedClaims.Any(claim => claim.Type == "role"));
    }

    private static (ProfileService Service, ProfileDataRequestContext Context) CreateProfileRequest(string[] requestedClaimTypes)
    {
        var user = new ApplicationUser { Id = "user-1", UserName = "alice" };
        var userManager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        userManager.FindByIdAsync(user.Id).Returns(user);
        userManager.GetRolesAsync(user).Returns(new List<string> { "RulesPublisher" });

        var subject = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.Id)], "test"));
        var context = new ProfileDataRequestContext
        {
            Subject = subject,
            RequestedClaimTypes = requestedClaimTypes
        };

        return (new ProfileService(userManager), context);
    }

    private static IConfiguration BuildConfiguration(bool includeAdmin = true)
    {
        var values = new Dictionary<string, string?>
        {
            ["BusinessRulesReaderSecret"] = "reader-secret",
            ["BusinessRulesApiSecret"] = "api-secret",
            ["BusinessRulesApiClient"] = "https://businessrules.test"
        };

        if (includeAdmin)
        {
            values["EShopAdminClient"] = "https://admin.test";
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [TestMethod]
    [DataRow("/connect/authorize?redirect_uri=https%3A%2F%2Fweb.test%2Fsignin-oidc&scope=openid", "https://web.test/")]
    [DataRow("/connect/authorize?client_id=webapp", "")]
    public void RedirectServiceExtractsConfiguredRedirect(string returnUrl, string expected)
    {
        var service = new RedirectService();

        Assert.AreEqual(expected, service.ExtractRedirectUriFromReturnUrl(returnUrl));
    }
}
