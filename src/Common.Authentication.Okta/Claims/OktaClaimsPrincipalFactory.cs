using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;

namespace Common.Authentication.Okta.Claims
{
    /// <summary>
    /// Maps the tenant-specific access-token claims onto standard .NET claim types so the rest of
    /// the application can rely on <see cref="ClaimsPrincipal"/> as usual:
    /// <list type="bullet">
    ///   <item><c>sub</c> (e.g. <c>first.lastName</c>) → <see cref="ClaimTypes.Name"/></item>
    ///   <item><c>SAMAccount</c> → <see cref="ClaimTypes.Email"/> and <see cref="CustomClaimTypes.SamAccount"/></item>
    ///   <item><c>empID</c> → <see cref="CustomClaimTypes.AdEmployeeId"/></item>
    /// </list>
    /// </summary>
    public sealed class OktaClaimsPrincipalFactory : IClaimsPrincipalFactory
    {
        private const string AuthenticationType = "Okta";

        private static readonly HashSet<string> DroppedSourceClaims = new(StringComparer.Ordinal)
        {
            "iat", "exp", "nbf", "jti", "uid", "ver",
        };

        private readonly OktaAuthenticationOptions options;

        public OktaClaimsPrincipalFactory(OktaAuthenticationOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public ClaimsPrincipal Create(ClaimsIdentity validatedAccessTokenIdentity)
        {
            if (validatedAccessTokenIdentity is null)
            {
                throw new ArgumentNullException(nameof(validatedAccessTokenIdentity));
            }

            var name = GetRequired(validatedAccessTokenIdentity, options.NameSourceClaim);
            var email = validatedAccessTokenIdentity.FindFirst(options.EmailSourceClaim)?.Value;
            var employeeId = validatedAccessTokenIdentity.FindFirst(options.EmployeeIdSourceClaim)?.Value;

            var claims = new List<Claim> { new(ClaimTypes.Name, name) };

            if (!string.IsNullOrWhiteSpace(email))
            {
                claims.Add(new Claim(ClaimTypes.Email, email));
                claims.Add(new Claim(CustomClaimTypes.SamAccount, email));
            }

            if (!string.IsNullOrWhiteSpace(employeeId))
            {
                claims.Add(new Claim(CustomClaimTypes.AdEmployeeId, employeeId));
            }

            // Carry through the remaining non-mapped, non-noise claims (iss, cid, scp, groups, ...).
            var mapped = new HashSet<string>(StringComparer.Ordinal)
            {
                options.NameSourceClaim, options.EmailSourceClaim, options.EmployeeIdSourceClaim,
            };

            foreach (var claim in validatedAccessTokenIdentity.Claims)
            {
                if (mapped.Contains(claim.Type) || DroppedSourceClaims.Contains(claim.Type))
                {
                    continue;
                }

                claims.Add(new Claim(claim.Type, claim.Value, claim.ValueType, claim.Issuer));
            }

            var identity = new ClaimsIdentity(
                claims,
                AuthenticationType,
                nameType: ClaimTypes.Name,
                roleType: ClaimTypes.Role);

            Debug.WriteLine(
                $"[OktaClaimsPrincipalFactory] Built principal Name='{name}', Email='{email ?? "(none)"}', " +
                $"AdEmployeeId='{employeeId ?? "(none)"}', totalClaims={claims.Count}.");

            return new ClaimsPrincipal(identity);
        }

        private string GetRequired(ClaimsIdentity identity, string claimType)
        {
            var value = identity.FindFirst(claimType)?.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"The access token does not contain the required '{claimType}' claim used for the user name.");
            }

            return value;
        }
    }
}
