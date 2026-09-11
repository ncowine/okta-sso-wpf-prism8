using System.Security.Claims;
using Common.Authentication.Okta.Tokens;

namespace Common.Authentication.Okta
{
    /// <summary>Outcome of a sign-in attempt (interactive or silent).</summary>
    public sealed class AuthenticationResult
    {
        private AuthenticationResult()
        {
        }

        public bool Success { get; private set; }

        public ClaimsPrincipal? Principal { get; private set; }

        public TokenSet? Tokens { get; private set; }

        public string? Error { get; private set; }

        public static AuthenticationResult Ok(ClaimsPrincipal principal, TokenSet tokens) => new()
        {
            Success = true,
            Principal = principal,
            Tokens = tokens,
        };

        public static AuthenticationResult Fail(string error) => new()
        {
            Success = false,
            Error = error,
        };
    }
}
