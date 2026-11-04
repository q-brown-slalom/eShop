using System.Net.Http.Headers;

namespace eShop.BusinessRules.Client.Auth;

/// <summary>Adds a client-credentials bearer token (plan decision D2) to every outgoing request.</summary>
public sealed class ServiceTokenHandler(ServiceTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
