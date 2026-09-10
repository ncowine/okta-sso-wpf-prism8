namespace DemoApi;

/// <summary>Bound from the <c>Api</c> section of appsettings.json.</summary>
public sealed class DemoApiOptions
{
    /// <summary>Scheme + host to listen on.</summary>
    public string PublicUrl { get; set; } = "http://localhost:5006";

    /// <summary>Token issuer / OIDC authority (the dummy IdP or a real Okta auth server).</summary>
    public string Authority { get; set; } = "http://localhost:5005/oauth2/default";

    /// <summary>Expected <c>aud</c> of incoming access tokens.</summary>
    public string Audience { get; set; } = "api://default";

    /// <summary>Set false for the http localhost dummy IdP; true for real Okta.</summary>
    public bool RequireHttpsMetadata { get; set; }

    /// <summary>This API's own client id, used as the actor when exchanging the user's token.</summary>
    public string ClientId { get; set; } = "demo-api-a";

    /// <summary>Base URL of the downstream third-party API (DemoApiB).</summary>
    public string DownstreamApiUrl { get; set; } = "http://localhost:5007";

    /// <summary>Audience to request when exchanging the user's token for a downstream call.</summary>
    public string DownstreamAudience { get; set; } = "api://demo-api-b";

    public string TokenEndpoint => $"{Authority.TrimEnd('/')}/v1/token";
}
