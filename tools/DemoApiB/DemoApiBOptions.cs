namespace DemoApiB;

/// <summary>Bound from the <c>Api</c> section of appsettings.json.</summary>
public sealed class DemoApiBOptions
{
    public string PublicUrl { get; set; } = "http://localhost:5007";

    /// <summary>Token issuer / OIDC authority (the same IdP DemoApi trusts).</summary>
    public string Authority { get; set; } = "http://localhost:5005/oauth2/default";

    /// <summary>Expected <c>aud</c> of incoming access tokens (distinct from DemoApi's audience).</summary>
    public string Audience { get; set; } = "api://demo-api-b";

    public bool RequireHttpsMetadata { get; set; }
}
