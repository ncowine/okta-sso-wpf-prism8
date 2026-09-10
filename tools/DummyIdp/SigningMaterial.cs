using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace DummyIdp;

/// <summary>An in-memory RSA key pair used to sign tokens and publish a JWKS document.</summary>
public sealed class SigningMaterial
{
    public SigningMaterial()
    {
        var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString("N") };
        SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        KeyId = key.KeyId;

        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key);
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        PublicJwk = jwk;
    }

    public SigningCredentials SigningCredentials { get; }

    public string KeyId { get; }

    public JsonWebKey PublicJwk { get; }

    public object BuildJwksDocument() => new
    {
        keys = new[]
        {
            new
            {
                kty = PublicJwk.Kty,
                use = PublicJwk.Use,
                alg = PublicJwk.Alg,
                kid = PublicJwk.Kid,
                n = PublicJwk.N,
                e = PublicJwk.E,
            },
        },
    };
}
