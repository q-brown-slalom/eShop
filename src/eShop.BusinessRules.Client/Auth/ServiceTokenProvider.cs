using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace eShop.BusinessRules.Client.Auth;

/// <summary>
/// Obtains client-credentials access tokens for one scope from Identity.API and caches each until
/// 60 s before it expires. Registered as a keyed singleton per scope so all HTTP handlers share it.
/// </summary>
public sealed class ServiceTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ServiceClientOptions> options,
    string scope,
    TimeProvider timeProvider)
{
    public const string HttpClientName = "eShop.BusinessRules.Client.ServiceToken";

    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CachedToken? _cached;

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (TryGetCached() is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (TryGetCached() is { } refreshed)
            {
                return refreshed;
            }

            var response = await RequestTokenAsync(ct);
            var refreshAt = timeProvider.GetUtcNow() + TimeSpan.FromSeconds(response.ExpiresIn) - ExpiryMargin;
            Volatile.Write(ref _cached, new CachedToken(response.AccessToken, refreshAt));
            return response.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string? TryGetCached()
    {
        var cached = Volatile.Read(ref _cached);
        return cached is not null && timeProvider.GetUtcNow() < cached.RefreshAt ? cached.AccessToken : null;
    }

    private async Task<TokenResponse> RequestTokenAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrEmpty(settings.IdentityUrl))
        {
            throw new InvalidOperationException("Identity:Url is not configured.");
        }

        var tokenEndpoint = new Uri(new Uri(settings.IdentityUrl.TrimEnd('/') + "/"), "connect/token");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["scope"] = scope,
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
        });

        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsync(tokenEndpoint, content, ct);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new InvalidOperationException($"Identity.API returned no access token for scope '{scope}'.");
        }

        return token;
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset RefreshAt);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
