using System;

namespace Common.Authentication.Okta
{
    /// <summary>
    /// Strongly-typed configuration for the Okta SSO flow. Populated by the host application
    /// (in this demo, from <c>App.config</c> &lt;appSettings&gt;).
    /// </summary>
    public sealed class OktaAuthenticationOptions
    {
        /// <summary>Okta org base URL, e.g. <c>https://dev-123456.okta.com</c>.</summary>
        public string OktaDomain { get; set; } = string.Empty;

        /// <summary>
        /// Custom authorization server id. For a tenant that customizes access-token claims this
        /// is normally <c>default</c>, giving an issuer of <c>{OktaDomain}/oauth2/default</c>.
        /// </summary>
        public string AuthorizationServerId { get; set; } = "default";

        /// <summary>OIDC client (application) id from the Okta admin console.</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>Expected <c>aud</c> of the access token, e.g. <c>api://default</c>.</summary>
        public string Audience { get; set; } = "api://default";

        /// <summary>Redirect URI registered in Okta. This demo uses a custom scheme.</summary>
        public string RedirectUri { get; set; } = "app://auth/callback";

        /// <summary>Post-logout redirect URI registered in Okta.</summary>
        public string PostLogoutRedirectUri { get; set; } = "app://auth/callback";

        /// <summary>Requested scopes. <c>offline_access</c> is required for silent refresh.</summary>
        public string Scope { get; set; } = "openid profile email offline_access";

        /// <summary>The scheme portion of <see cref="RedirectUri"/> that is registered with Windows.</summary>
        public string CustomUriScheme { get; set; } = "app";

        /// <summary>Allowed clock skew when validating token lifetime.</summary>
        public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>Access-token claim that carries the user name (value shaped like <c>first.lastName</c>).</summary>
        public string NameSourceClaim { get; set; } = "sub";

        /// <summary>Access-token claim that carries the user's email / login id.</summary>
        public string EmailSourceClaim { get; set; } = "SAMAccount";

        /// <summary>Access-token claim that carries the AD employee id.</summary>
        public string EmployeeIdSourceClaim { get; set; } = "empID";

        /// <summary>When true, the access token's <c>cid</c> claim must equal <see cref="ClientId"/>.</summary>
        public bool ValidateTokenClientId { get; set; } = true;

        /// <summary>How long the interactive browser sign-in may take before it is abandoned.</summary>
        public TimeSpan InteractiveTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// DEV ONLY. When true, allows an <c>http://</c> authority so the app can run against the
        /// bundled dummy IdP on localhost. Token signature, issuer and audience are still validated.
        /// Never enable this against a real Okta tenant.
        /// </summary>
        public bool AllowInsecureHttp { get; set; }

        /// <summary>Issuer of both the discovery document and the tokens.</summary>
        public string Authority =>
            $"{OktaDomain.TrimEnd('/')}/oauth2/{AuthorizationServerId}";

        /// <summary>OIDC discovery metadata address.</summary>
        public string MetadataAddress => $"{Authority}/.well-known/openid-configuration";

        /// <summary>Throws <see cref="InvalidOperationException"/> if a required value is missing.</summary>
        public void Validate()
        {
            RequireValue(OktaDomain, nameof(OktaDomain));
            RequireValue(ClientId, nameof(ClientId));
            RequireValue(Audience, nameof(Audience));
            RequireValue(RedirectUri, nameof(RedirectUri));
            RequireValue(Scope, nameof(Scope));
            RequireValue(CustomUriScheme, nameof(CustomUriScheme));

            if (!Uri.TryCreate(OktaDomain, UriKind.Absolute, out var domainUri))
            {
                throw new InvalidOperationException(
                    $"Okta:{nameof(OktaDomain)} must be an absolute URL.");
            }

            var schemeOk = domainUri.Scheme == Uri.UriSchemeHttps ||
                           (AllowInsecureHttp && domainUri.Scheme == Uri.UriSchemeHttp);
            if (!schemeOk)
            {
                throw new InvalidOperationException(AllowInsecureHttp
                    ? $"Okta:{nameof(OktaDomain)} must be an absolute http or https URL."
                    : $"Okta:{nameof(OktaDomain)} must be an absolute https URL.");
            }

            if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out var redirectUri))
            {
                throw new InvalidOperationException($"Okta:{nameof(RedirectUri)} must be an absolute URI.");
            }

            if (!string.Equals(redirectUri.Scheme, CustomUriScheme, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Okta:{nameof(RedirectUri)} scheme '{redirectUri.Scheme}' does not match " +
                    $"Okta:{nameof(CustomUriScheme)} '{CustomUriScheme}'.");
            }
        }

        private static void RequireValue(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Okta:{name} is required but was not configured.");
            }
        }
    }
}
