using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Common.Authentication.Okta.Tokens
{
    /// <summary>
    /// Validates Okta access tokens using the tenant's published OIDC metadata. Signing keys are
    /// fetched from the JWKS endpoint and cached / rotated automatically by
    /// <see cref="ConfigurationManager{T}"/>.
    /// </summary>
    public sealed class OktaAccessTokenValidator : IAccessTokenValidator
    {
        private readonly OktaAuthenticationOptions options;
        private readonly ConfigurationManager<OpenIdConnectConfiguration> configurationManager;
        private readonly JsonWebTokenHandler handler = new();

        public OktaAccessTokenValidator(OktaAuthenticationOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));

            configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                options.MetadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = !options.AllowInsecureHttp });
        }

        public async Task<ClaimsIdentity> ValidateAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new ArgumentException("Access token is required.", nameof(accessToken));
            }

            var configuration = await configurationManager
                .GetConfigurationAsync(cancellationToken)
                .ConfigureAwait(false);

            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = configuration.Issuer,
                ValidateAudience = true,
                ValidAudiences = new[] { options.Audience },
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = configuration.SigningKeys,
                ValidateLifetime = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = options.ClockSkew,
                NameClaimType = options.NameSourceClaim,
            };

            var result = await handler.ValidateTokenAsync(accessToken, parameters).ConfigureAwait(false);

            if (!result.IsValid)
            {
                Debug.WriteLine($"[OktaAccessTokenValidator] Access token rejected: {result.Exception?.Message}");
                throw new SecurityTokenValidationException("The Okta access token failed validation.", result.Exception);
            }

            var identity = result.ClaimsIdentity
                ?? throw new SecurityTokenValidationException("Access token produced no claims identity.");

            if (options.ValidateTokenClientId)
            {
                var clientId = identity.FindFirst("cid")?.Value ?? identity.FindFirst("client_id")?.Value;
                if (!string.Equals(clientId, options.ClientId, StringComparison.Ordinal))
                {
                    throw new SecurityTokenValidationException(
                        "Access token 'cid' claim does not match the configured client id.");
                }
            }

            Debug.WriteLine(
                $"[OktaAccessTokenValidator] Access token valid. Subject='{identity.FindFirst(options.NameSourceClaim)?.Value}', " +
                $"claims=[{string.Join(", ", identity.Claims.Select(c => c.Type))}].");

            return identity;
        }
    }
}
