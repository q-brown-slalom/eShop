namespace eShop.Ordering.FunctionalTests;

class AutoAuthorizeMiddleware
{
    public const string IDENTITY_ID = "9e3163b9-1ae6-4652-9dc6-7898ab7b7a00";
    public const string TestScopesHeader = "x-test-scopes";
    public const string AnonymousHeader = "x-test-anonymous";

    private readonly RequestDelegate _next;

    public AutoAuthorizeMiddleware(RequestDelegate rd)
    {
        _next = rd;
    }

    public async Task Invoke(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.ContainsKey(AnonymousHeader))
        {
            await _next.Invoke(httpContext);
            return;
        }

        var identity = new ClaimsIdentity("cookies");

        identity.AddClaim(new Claim("sub", IDENTITY_ID));
        identity.AddClaim(new Claim("unique_name", IDENTITY_ID));
        identity.AddClaim(new Claim(ClaimTypes.Name, IDENTITY_ID));

        if (httpContext.Request.Headers.TryGetValue(TestScopesHeader, out var scopes))
        {
            foreach (var scope in scopes.SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            {
                identity.AddClaim(new Claim("scope", scope));
            }
        }

        // Make the fake identity the primary one so policy failures yield 403, as with a real token.
        httpContext.User = new ClaimsPrincipal(identity);

        await _next.Invoke(httpContext);
    }
}
