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

        public bool Success { get; private init; }

        public ClaimsPrincipal? Principal { get; private init; }

        public TokenSet? Tokens { get; private init; }

        public string? Error { get; private init; }

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
