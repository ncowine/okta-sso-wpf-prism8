using System;

namespace Common.Authentication.Okta.Oidc
{
    /// <summary>Outcome of a token-endpoint call (authorization code exchange or refresh).</summary>
    public sealed class OidcTokenResult
    {
        private OidcTokenResult()
        {
        }

        public bool IsError { get; private init; }

        public string? Error { get; private init; }

        public string? ErrorDescription { get; private init; }

        public string AccessToken { get; private init; } = string.Empty;

        /// <summary>The ID token, when the token response included one.</summary>
        public string? IdentityToken { get; private init; }

        /// <summary>Null when <c>offline_access</c> was not granted.</summary>
        public string? RefreshToken { get; private init; }

        public DateTimeOffset AccessTokenExpiration { get; private init; }

        public static OidcTokenResult Ok(
            string accessToken,
            string? identityToken,
            string? refreshToken,
            DateTimeOffset accessTokenExpiration) => new()
            {
                AccessToken = accessToken,
                IdentityToken = identityToken,
                RefreshToken = refreshToken,
                AccessTokenExpiration = accessTokenExpiration,
            };

        public static OidcTokenResult Fail(string? error, string? errorDescription = null) => new()
        {
            IsError = true,
            Error = error,
            ErrorDescription = errorDescription,
        };
    }
}
