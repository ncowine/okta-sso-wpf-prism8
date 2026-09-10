using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DummyIdp;

/// <summary>Builds signed access and ID tokens that mirror the shape of the real tenant's tokens.</summary>
public sealed class TokenService
{
    private readonly DummyIdpOptions options;
    private readonly SigningMaterial signing;
    private readonly JsonWebTokenHandler handler = new();

    public TokenService(IOptions<DummyIdpOptions> options, SigningMaterial signing)
    {
        this.options = options.Value;
        this.signing = signing;
    }

    public int AccessTokenLifetimeSeconds => options.AccessTokenLifetimeMinutes * 60;

    /// <summary>
    /// Access token: a JWT whose custom claims (<c>sub</c>, <c>SAMAccount</c>, <c>empID</c>) are the
    /// only place the identity data lives — exactly the scenario the WPF app handles. Carries every
    /// configured audience so one token is accepted by DemoApi and (directly) by DemoApiB.
    /// </summary>
    public string CreateAccessToken(string clientId, string scope, DummyUser user) =>
        Create(options.Audiences, clientId, scope, user, actor: null);

    /// <summary>
    /// Delegated access token from an RFC 8693 token exchange: audience is the single requested
    /// resource and an <c>act</c> claim records the client acting on the user's behalf.
    /// </summary>
    public string CreateDelegatedAccessToken(string actorClientId, string audience, string scope, DummyUser user) =>
        Create(new List<string> { audience }, actorClientId, scope, user, actor: actorClientId);

    private string Create(IList<string> audiences, string clientId, string scope, DummyUser user, string? actor)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Sub,
            ["SAMAccount"] = user.SamAccount,
            ["empID"] = user.EmpId,
            ["aud"] = audiences.Count == 1 ? audiences[0] : audiences.ToArray(),
            ["cid"] = clientId,
            ["client_id"] = clientId,
            ["scp"] = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            ["uid"] = user.Sub,
            ["jti"] = Guid.NewGuid().ToString("N"),
        };

        if (actor is not null)
        {
            claims["act"] = new Dictionary<string, object> { ["sub"] = actor };
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddSeconds(AccessTokenLifetimeSeconds).UtcDateTime,
            SigningCredentials = signing.SigningCredentials,
            TokenType = "at+jwt",
            Claims = claims,
        };

        return handler.CreateToken(descriptor);
    }

    /// <summary>ID token: standard OIDC claims only (the app does not read identity data from here).</summary>
    public string CreateIdToken(string clientId, string? nonce, DummyUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Sub,
            ["auth_time"] = now.ToUnixTimeSeconds(),
        };

        if (!string.IsNullOrEmpty(nonce))
        {
            claims["nonce"] = nonce;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = clientId,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddSeconds(AccessTokenLifetimeSeconds).UtcDateTime,
            SigningCredentials = signing.SigningCredentials,
            Claims = claims,
        };

        return handler.CreateToken(descriptor);
    }

    /// <summary>Validates a token this IdP issued (signature + issuer + lifetime). Returns its user.</summary>
    public async Task<DummyUser?> ValidateOwnAccessTokenAsync(string token)
    {
        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signing.SigningCredentials.Key,
        });

        if (!result.IsValid)
        {
            Console.WriteLine($"[token-exchange] subject_token rejected: {result.Exception?.Message}");
            return null;
        }

        var identity = result.ClaimsIdentity;
        var sub = identity.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(sub))
        {
            return null;
        }

        return new DummyUser
        {
            Sub = sub,
            SamAccount = identity.FindFirst("SAMAccount")?.Value ?? "",
            EmpId = identity.FindFirst("empID")?.Value ?? "",
            DisplayName = sub,
        };
    }
}
