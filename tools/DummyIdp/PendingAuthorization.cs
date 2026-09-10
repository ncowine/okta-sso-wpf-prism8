namespace DummyIdp;

/// <summary>State captured at the authorize step and redeemed once at the token endpoint.</summary>
public sealed record PendingAuthorization(
    string ClientId,
    string RedirectUri,
    string CodeChallenge,
    string? Nonce,
    string Scope,
    DummyUser User,
    DateTimeOffset CreatedAt);
