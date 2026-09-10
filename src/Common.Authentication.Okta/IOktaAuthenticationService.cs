using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Authentication.Okta
{
    /// <summary>
    /// Entry point for Okta SSO: interactive sign-in via the system browser, silent
    /// refresh-token sign-in, and sign-out. Holds the current <see cref="ClaimsPrincipal"/>.
    /// </summary>
    public interface IOktaAuthenticationService
    {
        /// <summary>The principal for the signed-in user, or <c>null</c> when signed out.</summary>
        ClaimsPrincipal? CurrentPrincipal { get; }

        bool IsAuthenticated { get; }

        event EventHandler<AuthenticationStateChangedEventArgs>? StateChanged;

        /// <summary>Runs the Authorization Code + PKCE flow in the user's default browser.</summary>
        Task<AuthenticationResult> SignInInteractiveAsync(CancellationToken cancellationToken = default);

        /// <summary>Attempts to restore a session from the stored refresh token without any UI.</summary>
        Task<AuthenticationResult> TrySignInSilentAsync(CancellationToken cancellationToken = default);

        /// <summary>Clears local session state and best-effort ends the Okta session.</summary>
        Task SignOutAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a currently-valid access token for calling downstream APIs, refreshing it with the
        /// stored refresh token if it is at or near expiry. Returns <c>null</c> when signed out.
        /// </summary>
        Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    }
}
