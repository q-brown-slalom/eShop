namespace eShop.BusinessRules.Client.Auth;

/// <summary>OAuth client-credentials settings, bound from <c>ServiceClient:ClientId</c>, <c>ServiceClient:ClientSecret</c> and <c>Identity:Url</c>.</summary>
public sealed class ServiceClientOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string IdentityUrl { get; set; } = string.Empty;
}
