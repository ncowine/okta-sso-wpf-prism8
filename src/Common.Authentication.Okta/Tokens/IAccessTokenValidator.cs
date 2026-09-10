using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Authentication.Okta.Tokens
{
    /// <summary>Validates an Okta access token (JWT) and returns its verified claims.</summary>
    public interface IAccessTokenValidator
    {
        /// <summary>
        /// Validates signature (against the Okta JWKS), issuer, audience and lifetime.
        /// Throws if the token is not valid.
        /// </summary>
        Task<ClaimsIdentity> ValidateAsync(string accessToken, CancellationToken cancellationToken = default);
    }
}
