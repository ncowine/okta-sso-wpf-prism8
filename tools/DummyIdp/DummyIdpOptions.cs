namespace DummyIdp;

/// <summary>Configuration for the dummy IdP, bound from the <c>DummyIdp</c> section of appsettings.json.</summary>
public sealed class DummyIdpOptions
{
    /// <summary>Scheme + host the IdP is reachable at, e.g. <c>http://localhost:5005</c>.</summary>
    public string PublicUrl { get; set; } = "http://localhost:5005";

    /// <summary>Authorization server id segment. Issuer = <c>{PublicUrl}/oauth2/{AuthorizationServerId}</c>.</summary>
    public string AuthorizationServerId { get; set; } = "default";

    /// <summary>
    /// Audiences stamped into a normal access token's <c>aud</c> claim. The first is the "primary"
    /// API; extra entries let one token be accepted by several APIs (e.g. DemoApi + DemoApiB).
    /// (Empty here so config binding replaces rather than appends; defaulted in Program.cs.)
    /// </summary>
    public List<string> Audiences { get; set; } = new();

    /// <summary>Audiences a token-exchange request is allowed to target.</summary>
    public List<string> ExchangeAudiences { get; set; } = new();

    /// <summary>Primary audience (first of <see cref="Audiences"/>).</summary>
    public string Audience => Audiences.Count > 0 ? Audiences[0] : "api://default";

    /// <summary>Access token lifetime.</summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// When true, <c>/authorize</c> skips the sign-in page and immediately approves the first
    /// user (handy for a hands-free demo). When false, the page with the user picker + Deny is shown.
    /// </summary>
    public bool AutoApprove { get; set; }

    /// <summary>Selectable test identities shown on the sign-in page.</summary>
    public List<DummyUser> Users { get; set; } = new();

    public string Issuer => $"{PublicUrl.TrimEnd('/')}/oauth2/{AuthorizationServerId}";

    public string BasePath => $"/oauth2/{AuthorizationServerId}";
}
