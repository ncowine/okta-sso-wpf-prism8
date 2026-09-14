namespace Common.Authentication.Okta.Claims
{
    /// <summary>
    /// Claim type URNs for the values that Okta places in the access token but that are
    /// not part of the standard OIDC ID token for this tenant.
    /// </summary>
    public static class CustomClaimTypes
    {
        /// <summary>Active Directory employee id (source access-token claim: <c>empID</c>).</summary>
        public const string AdEmployeeId = "urn:common-authentication-okta:ad-emp-id";

        /// <summary>Windows / AD account name (source access-token claim: <c>SAMAccount</c>).</summary>
        public const string SamAccount = "urn:common-authentication-okta:sam-account";
    }
}
