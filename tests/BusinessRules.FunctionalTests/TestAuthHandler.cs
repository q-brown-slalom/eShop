using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace eShop.BusinessRules.FunctionalTests;

/// <summary>
/// Authenticates every request from test headers so tests can choose the caller's scopes, roles and subject.
/// </summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string ScopesHeader = "x-test-scopes";
    public const string RolesHeader = "x-test-roles";
    public const string SubjectHeader = "x-test-sub";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(SchemeName, "name", "role");

        if (Request.Headers.TryGetValue(SubjectHeader, out var subject) && !string.IsNullOrEmpty(subject))
        {
            identity.AddClaim(new Claim("sub", subject.ToString()));
            identity.AddClaim(new Claim("name", subject.ToString()));
        }

        foreach (var scope in SplitHeader(ScopesHeader))
        {
            identity.AddClaim(new Claim("scope", scope));
        }

        foreach (var role in SplitHeader(RolesHeader))
        {
            identity.AddClaim(new Claim("role", role));
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private IEnumerable<string> SplitHeader(string name)
        => Request.Headers.TryGetValue(name, out var values)
            ? values.SelectMany(v => (v ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            : [];
}
