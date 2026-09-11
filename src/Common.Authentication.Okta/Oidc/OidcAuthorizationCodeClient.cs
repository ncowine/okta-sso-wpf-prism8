using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Authentication.Okta.Browser;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Common.Authentication.Okta.Oidc
{
    /// <summary>
    /// Minimal standards-based OIDC client: discovery, browser-driven Authorization Code + PKCE
    /// login, and refresh-token calls against the token endpoint. Built entirely on
    /// <see cref="HttpClient"/> and <c>Microsoft.IdentityModel.Protocols.OpenIdConnect</c>
    /// (already a dependency for access-token validation) — no third-party OIDC client package.
    /// </summary>
    public sealed class OidcAuthorizationCodeClient
    {
        private static readonly HttpClient Http = new();

        private readonly OktaAuthenticationOptions options;
        private readonly IBrowser browser;
        private readonly ConfigurationManager<OpenIdConnectConfiguration> configurationManager;

        public OidcAuthorizationCodeClient(OktaAuthenticationOptions options, IBrowser browser)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.browser = browser ?? throw new ArgumentNullException(nameof(browser));

            configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                options.MetadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = !options.AllowInsecureHttp });
        }

        /// <summary>Runs the browser-based Authorization Code + PKCE flow.</summary>
        public async Task<OidcTokenResult> LoginAsync(CancellationToken cancellationToken = default)
        {
            var configuration = await configurationManager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

            var state = CreateRandomUrlSafeToken();
            var nonce = CreateRandomUrlSafeToken();
            var codeVerifier = CreateRandomUrlSafeToken();
            var codeChallenge = CreateCodeChallenge(codeVerifier);

            var authorizeUrl = BuildAuthorizeUrl(configuration.AuthorizationEndpoint, state, nonce, codeChallenge);

            var browserResult = await browser
                .InvokeAsync(new BrowserOptions(authorizeUrl, options.RedirectUri), cancellationToken)
                .ConfigureAwait(false);

            if (browserResult.ResultType != BrowserResultType.Success)
            {
                Debug.WriteLine($"[OidcAuthorizationCodeClient] Browser step did not succeed: {browserResult.ResultType}.");
                return OidcTokenResult.Fail(browserResult.ResultType.ToString(), browserResult.Error);
            }

            if (!TryParseCallback(browserResult.Response, state, out var code, out var callbackError))
            {
                return OidcTokenResult.Fail("invalid_callback", callbackError);
            }

            var parameters = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code!,
                ["redirect_uri"] = options.RedirectUri,
                ["client_id"] = options.ClientId,
                ["code_verifier"] = codeVerifier,
            };

            return await RequestTokenAsync(configuration, parameters, nonce, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Exchanges a refresh token for a new access token (and, if rotated, a new refresh token).</summary>
        public async Task<OidcTokenResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new ArgumentException("Refresh token is required.", nameof(refreshToken));
            }

            var configuration = await configurationManager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

            var parameters = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = options.ClientId,
            };

            return await RequestTokenAsync(configuration, parameters, expectedNonce: null, cancellationToken).ConfigureAwait(false);
        }

        private async Task<OidcTokenResult> RequestTokenAsync(
            OpenIdConnectConfiguration configuration,
            Dictionary<string, string> parameters,
            string? expectedNonce,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(parameters),
            };

            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;

            if (!response.IsSuccessStatusCode || root.TryGetProperty("error", out _))
            {
                var error = root.TryGetProperty("error", out var e) ? e.GetString() : response.ReasonPhrase;
                var description = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
                Debug.WriteLine($"[OidcAuthorizationCodeClient] Token request failed: {error} {description}");
                return OidcTokenResult.Fail(error, description);
            }

            var accessToken = root.GetProperty("access_token").GetString() ?? string.Empty;
            var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt64() : 0L;
            var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var identityToken = root.TryGetProperty("id_token", out var it) ? it.GetString() : null;

            if (expectedNonce is not null && identityToken is not null && !NonceMatches(identityToken, expectedNonce))
            {
                Debug.WriteLine("[OidcAuthorizationCodeClient] ID token 'nonce' did not match the request; rejecting.");
                return OidcTokenResult.Fail("invalid_nonce", "The ID token nonce did not match the request.");
            }

            return OidcTokenResult.Ok(accessToken, identityToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
        }

        private string BuildAuthorizeUrl(string authorizationEndpoint, string state, string nonce, string codeChallenge)
        {
            var query = new Dictionary<string, string>
            {
                ["client_id"] = options.ClientId,
                ["response_type"] = "code",
                ["scope"] = options.Scope,
                ["redirect_uri"] = options.RedirectUri,
                ["state"] = state,
                ["nonce"] = nonce,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
            };

            var queryString = string.Join(
                "&",
                query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

            return $"{authorizationEndpoint}?{queryString}";
        }

        private static bool TryParseCallback(string? callbackUri, string expectedState, out string? code, out string? error)
        {
            code = null;
            error = null;

            if (string.IsNullOrEmpty(callbackUri) || !Uri.TryCreate(callbackUri, UriKind.Absolute, out var uri))
            {
                error = "The browser did not return a valid callback URI.";
                return false;
            }

            var query = ParseQueryString(uri.Query);

            if (query.TryGetValue("error", out var oauthError))
            {
                query.TryGetValue("error_description", out var description);
                error = string.IsNullOrEmpty(description) ? oauthError : $"{oauthError}: {description}";
                return false;
            }

            if (!query.TryGetValue("state", out var returnedState) ||
                !string.Equals(returnedState, expectedState, StringComparison.Ordinal))
            {
                error = "The callback 'state' did not match the request; the sign-in may have been tampered with.";
                return false;
            }

            if (!query.TryGetValue("code", out var authorizationCode) || string.IsNullOrEmpty(authorizationCode))
            {
                error = "The callback did not include an authorization code.";
                return false;
            }

            code = authorizationCode;
            return true;
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var trimmed = query.TrimStart('?');
            if (trimmed.Length == 0)
            {
                return result;
            }

            foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(parts[0]);
                var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                result[key] = value;
            }

            return result;
        }

        private static bool NonceMatches(string identityToken, string expectedNonce)
        {
            try
            {
                var segments = identityToken.Split('.');
                if (segments.Length < 2)
                {
                    return false;
                }

                using var json = JsonDocument.Parse(Base64UrlDecode(segments[1]));
                var actualNonce = json.RootElement.TryGetProperty("nonce", out var n) ? n.GetString() : null;
                return string.Equals(actualNonce, expectedNonce, StringComparison.Ordinal);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                return false;
            }
        }

        private static string CreateRandomUrlSafeToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        private static string CreateCodeChallenge(string codeVerifier) =>
            Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

        private static string Base64UrlEncode(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] Base64UrlDecode(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = (padded.Length % 4) switch
            {
                2 => padded + "==",
                3 => padded + "=",
                _ => padded,
            };
            return Convert.FromBase64String(padded);
        }
    }
}
