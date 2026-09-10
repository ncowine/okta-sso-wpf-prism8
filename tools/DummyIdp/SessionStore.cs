using System.Collections.Concurrent;

namespace DummyIdp;

/// <summary>In-memory, process-lifetime storage for authorization codes and refresh tokens.</summary>
public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, PendingAuthorization> codes = new();
    private readonly ConcurrentDictionary<string, (string ClientId, string Scope, DummyUser User)> refreshTokens = new();

    public string IssueCode(PendingAuthorization pending)
    {
        var code = Guid.NewGuid().ToString("N");
        codes[code] = pending;
        return code;
    }

    public PendingAuthorization? RedeemCode(string code) =>
        codes.TryRemove(code, out var pending) ? pending : null;

    public string IssueRefreshToken(string clientId, string scope, DummyUser user)
    {
        var token = Guid.NewGuid().ToString("N");
        refreshTokens[token] = (clientId, scope, user);
        return token;
    }

    public (string ClientId, string Scope, DummyUser User)? RedeemRefreshToken(string token) =>
        refreshTokens.TryRemove(token, out var value) ? value : null;
}
