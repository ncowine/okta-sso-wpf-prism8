using System;
using System.Security.Claims;

namespace Common.Authentication.Okta
{
    /// <summary>Raised by <see cref="IOktaAuthenticationService"/> whenever the signed-in user changes.</summary>
    public sealed class AuthenticationStateChangedEventArgs : EventArgs
    {
        public AuthenticationStateChangedEventArgs(ClaimsPrincipal? principal)
        {
            Principal = principal;
        }

        public ClaimsPrincipal? Principal { get; }

        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    }
}
