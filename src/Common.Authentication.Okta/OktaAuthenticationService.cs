using System;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Common.Authentication.Okta.Claims;
using Common.Authentication.Okta.Tokens;
using IdentityModel.OidcClient;
using IdentityModel.OidcClient.Browser;
using Microsoft.Extensions.Logging;

namespace Common.Authentication.Okta
{
    /// <summary>
    /// Coordinates the standard OIDC library (<see cref="OidcClient"/>) with access-token validation,
    /// the tenant-specific claims mapping and encrypted token storage.
    /// </summary>
    public sealed class OktaAuthenticationService : IOktaAuthenticationService
    {
        private readonly OktaAuthenticationOptions options;
        private readonly IAccessTokenValidator accessTokenValidator;
        private readonly IClaimsPrincipalFactory principalFactory;
        private readonly ITokenStore tokenStore;
        private readonly OidcClient oidcClient;
        private readonly SemaphoreSlim gate = new(1, 1);

        public OktaAuthenticationService(
            OktaAuthenticationOptions options,
            IBrowser browser,
            IAccessTokenValidator accessTokenValidator,
            IClaimsPrincipalFactory principalFactory,
            ITokenStore tokenStore,
            ILoggerFactory loggerFactory)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.accessTokenValidator = accessTokenValidator ?? throw new ArgumentNullException(nameof(accessTokenValidator));
            this.principalFactory = principalFactory ?? throw new ArgumentNullException(nameof(principalFactory));
            this.tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));

            var oidcOptions = new OidcClientOptions
            {
                Authority = options.Authority,
                ClientId = options.ClientId,
                Scope = options.Scope,
                RedirectUri = options.RedirectUri,
                PostLogoutRedirectUri = options.PostLogoutRedirectUri,
                Browser = browser ?? throw new ArgumentNullException(nameof(browser)),
                LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory)),

                // The principal is built from the validated access token, so skip the userinfo call.
                LoadProfile = false,
            };

            if (options.AllowInsecureHttp)
            {
                // DEV ONLY: lets discovery/token calls hit the http://localhost dummy IdP.
                oidcOptions.Policy.Discovery.RequireHttps = false;
            }

            oidcClient = new OidcClient(oidcOptions);
        }

        private TokenSet? currentTokens;

        public ClaimsPrincipal? CurrentPrincipal { get; private set; }

        public bool IsAuthenticated => CurrentPrincipal?.Identity?.IsAuthenticated == true;

        public event EventHandler<AuthenticationStateChangedEventArgs>? StateChanged;

        public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                currentTokens ??= await tokenStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                if (currentTokens is null)
                {
                    return null;
                }

                var stillValid = DateTimeOffset.UtcNow < currentTokens.AccessTokenExpiresAt - TimeSpan.FromSeconds(60);
                if (stillValid || string.IsNullOrEmpty(currentTokens.RefreshToken))
                {
                    return currentTokens.AccessToken;
                }

                Debug.WriteLine("[OktaAuthenticationService] Access token near expiry; refreshing for API call.");
                var refresh = await oidcClient
                    .RefreshTokenAsync(currentTokens.RefreshToken, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (refresh.IsError)
                {
                    Debug.WriteLine($"[OktaAuthenticationService] Pre-call refresh failed: {refresh.Error}");
                    return currentTokens.AccessToken;
                }

                var result = await CompleteSignInAsync(
                    refresh.AccessToken,
                    string.IsNullOrEmpty(refresh.IdentityToken) ? currentTokens.IdToken : refresh.IdentityToken,
                    string.IsNullOrEmpty(refresh.RefreshToken) ? currentTokens.RefreshToken : refresh.RefreshToken,
                    refresh.AccessTokenExpiration,
                    cancellationToken).ConfigureAwait(false);

                return result.Success ? result.Tokens!.AccessToken : currentTokens.AccessToken;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OktaAuthenticationService] GetAccessTokenAsync threw: {ex.Message}");
                return currentTokens?.AccessToken;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<AuthenticationResult> SignInInteractiveAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Debug.WriteLine("[OktaAuthenticationService] Starting interactive sign-in.");
                var login = await oidcClient.LoginAsync(new LoginRequest(), cancellationToken).ConfigureAwait(false);

                if (login.IsError)
                {
                    Debug.WriteLine($"[OktaAuthenticationService] Interactive sign-in failed: {login.Error} {login.ErrorDescription}");
                    return AuthenticationResult.Fail(Describe(login.Error, login.ErrorDescription));
                }

                return await CompleteSignInAsync(
                    login.AccessToken,
                    login.IdentityToken,
                    login.RefreshToken,
                    login.AccessTokenExpiration,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OktaAuthenticationService] Interactive sign-in threw: {ex}");
                return AuthenticationResult.Fail(ex.Message);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<AuthenticationResult> TrySignInSilentAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var stored = await tokenStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                if (stored is null || string.IsNullOrEmpty(stored.RefreshToken))
                {
                    Debug.WriteLine("[OktaAuthenticationService] No stored refresh token; silent sign-in not possible.");
                    return AuthenticationResult.Fail("No stored session.");
                }

                Debug.WriteLine("[OktaAuthenticationService] Attempting silent sign-in with stored refresh token.");
                var refresh = await oidcClient
                    .RefreshTokenAsync(stored.RefreshToken, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (refresh.IsError)
                {
                    Debug.WriteLine($"[OktaAuthenticationService] Refresh failed: {refresh.Error}. Clearing stored tokens.");
                    await tokenStore.ClearAsync(cancellationToken).ConfigureAwait(false);
                    return AuthenticationResult.Fail(Describe(refresh.Error, refresh.ErrorDescription));
                }

                return await CompleteSignInAsync(
                    refresh.AccessToken,
                    string.IsNullOrEmpty(refresh.IdentityToken) ? stored.IdToken : refresh.IdentityToken,
                    string.IsNullOrEmpty(refresh.RefreshToken) ? stored.RefreshToken : refresh.RefreshToken,
                    refresh.AccessTokenExpiration,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OktaAuthenticationService] Silent sign-in threw: {ex}");
                return AuthenticationResult.Fail(ex.Message);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Clear the local session immediately. We deliberately do NOT open the browser for a
                // provider-side end-session round trip here: it blocks the UI on an external browser,
                // and the app forces a fresh interactive sign-in straight afterwards anyway. A real
                // deployment that must kill the IdP session can call oidcClient.LogoutAsync(...).
                await tokenStore.ClearAsync(cancellationToken).ConfigureAwait(false);
                currentTokens = null;
                CurrentPrincipal = null;
                Debug.WriteLine("[OktaAuthenticationService] Signed out; local session cleared.");
                RaiseStateChanged();
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<AuthenticationResult> CompleteSignInAsync(
            string accessToken,
            string? idToken,
            string? refreshToken,
            DateTimeOffset accessTokenExpiration,
            CancellationToken cancellationToken)
        {
            var validatedIdentity = await accessTokenValidator
                .ValidateAsync(accessToken, cancellationToken)
                .ConfigureAwait(false);

            var principal = principalFactory.Create(validatedIdentity);

            var tokens = new TokenSet
            {
                AccessToken = accessToken,
                IdToken = idToken,
                RefreshToken = refreshToken,
                AccessTokenExpiresAt = accessTokenExpiration,
            };

            await tokenStore.SaveAsync(tokens, cancellationToken).ConfigureAwait(false);

            currentTokens = tokens;
            CurrentPrincipal = principal;
            Debug.WriteLine($"[OktaAuthenticationService] Signed in as '{principal.Identity?.Name}'.");
            RaiseStateChanged();

            return AuthenticationResult.Ok(principal, tokens);
        }

        private void RaiseStateChanged() =>
            StateChanged?.Invoke(this, new AuthenticationStateChangedEventArgs(CurrentPrincipal));

        private static string Describe(string? error, string? description) =>
            string.IsNullOrWhiteSpace(description) ? (error ?? "Unknown error") : $"{error}: {description}";
    }
}
