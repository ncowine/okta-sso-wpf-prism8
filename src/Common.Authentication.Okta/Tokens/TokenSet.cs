using System;

namespace Common.Authentication.Okta.Tokens
{
    /// <summary>
    /// The tokens returned by Okta for the current session. Persisted (encrypted) so the
    /// session can be silently restored on the next launch.
    /// </summary>
    public sealed class TokenSet
    {
        /// <summary>The access token (a JWT for this tenant) whose claims drive the principal.</summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>The ID token, retained for use as <c>id_token_hint</c> during sign-out.</summary>
        public string? IdToken { get; set; }

        /// <summary>The refresh token used for silent renewal. Null when <c>offline_access</c> was not granted.</summary>
        public string? RefreshToken { get; set; }

        /// <summary>Absolute expiry of <see cref="AccessToken"/>.</summary>
        public DateTimeOffset AccessTokenExpiresAt { get; set; }
    }
}
