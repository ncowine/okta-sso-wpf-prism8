using System.Security.Claims;

namespace Common.Authentication.Okta.Claims
{
    /// <summary>Builds the application <see cref="ClaimsPrincipal"/> from a validated access-token identity.</summary>
    public interface IClaimsPrincipalFactory
    {
        ClaimsPrincipal Create(ClaimsIdentity validatedAccessTokenIdentity);
    }
}
