using System.Security.Cryptography;
using System.Text;
using DummyIdp;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Load appsettings.json from next to the assembly, not the (arbitrary) launch directory.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.Configure<DummyIdpOptions>(builder.Configuration.GetSection("DummyIdp"));
builder.Services.AddSingleton<SigningMaterial>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<TokenService>();

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<DummyIdpOptions>>().Value;
if (options.Users.Count == 0)
{
    options.Users.Add(new DummyUser
    {
        Sub = "jane.doe", SamAccount = "jane.doe@example.com", EmpId = "AD100234", DisplayName = "Jane Doe",
    });
}

if (options.Audiences.Count == 0)
{
    options.Audiences.Add("api://default");
}

if (options.ExchangeAudiences.Count == 0)
{
    options.ExchangeAudiences.Add("api://demo-api-b");
}

var store = app.Services.GetRequiredService<SessionStore>();
var tokens = app.Services.GetRequiredService<TokenService>();
var signing = app.Services.GetRequiredService<SigningMaterial>();
var b = options.BasePath;

app.MapGet("/", () => Results.Content(
    $"""
    Dummy Okta-style IdP.
    Issuer:    {options.Issuer}
    Discovery: {options.Issuer}/.well-known/openid-configuration
    Users:     {string.Join(", ", options.Users.Select(u => u.Sub))}
    """, "text/plain"));

// ---- Discovery ------------------------------------------------------------
app.MapGet($"{b}/.well-known/openid-configuration", () => Results.Json(new
{
    issuer = options.Issuer,
    authorization_endpoint = $"{options.Issuer}/v1/authorize",
    token_endpoint = $"{options.Issuer}/v1/token",
    jwks_uri = $"{options.Issuer}/v1/keys",
    end_session_endpoint = $"{options.Issuer}/v1/logout",
    response_types_supported = new[] { "code" },
    response_modes_supported = new[] { "query" },
    grant_types_supported = new[]
    {
        "authorization_code", "refresh_token", "urn:ietf:params:oauth:grant-type:token-exchange",
    },
    subject_types_supported = new[] { "public" },
    id_token_signing_alg_values_supported = new[] { "RS256" },
    scopes_supported = new[] { "openid", "profile", "email", "offline_access" },
    token_endpoint_auth_methods_supported = new[] { "none" },
    claims_supported = new[] { "sub", "SAMAccount", "empID", "iss", "aud", "exp", "iat" },
    code_challenge_methods_supported = new[] { "S256" },
}));

// ---- JWKS ---------------------------------------------------------------------
app.MapGet($"{b}/v1/keys", () => Results.Json(signing.BuildJwksDocument()));

// ---- Authorize (renders a picker, or auto-approves) ------------------------
app.MapGet($"{b}/v1/authorize", (HttpRequest request) =>
{
    var q = request.Query;
    var redirectUri = q["redirect_uri"].ToString();
    var state = q["state"].ToString();

    if (options.AutoApprove && !string.IsNullOrEmpty(redirectUri))
    {
        return ApproveRedirect(
            store, options.Users[0], redirectUri, state,
            q["client_id"].ToString(), q["scope"].ToString(),
            q["nonce"].ToString(), q["code_challenge"].ToString());
    }

    string F(string name) => System.Net.WebUtility.HtmlEncode(q[name].ToString());

    var userRadios = string.Join("", options.Users.Select((u, i) =>
        $"""
        <label class="user"><input type="radio" name="userIndex" value="{i}" {(i == 0 ? "checked" : "")}>
        <span><b>{System.Net.WebUtility.HtmlEncode(u.DisplayName)}</b><br>
        sub={System.Net.WebUtility.HtmlEncode(u.Sub)} &middot; SAMAccount={System.Net.WebUtility.HtmlEncode(u.SamAccount)} &middot; empID={System.Net.WebUtility.HtmlEncode(u.EmpId)}</span></label>
        """));

    var html = $$"""
    <!doctype html><html><head><meta charset="utf-8"><title>Dummy IdP sign-in</title>
    <style>
      body{font:14px/1.5 system-ui,Segoe UI,sans-serif;background:#f3f4f6;margin:0;padding:40px}
      .card{max-width:460px;margin:0 auto;background:#fff;border:1px solid #e1e4e8;border-radius:10px;padding:24px}
      h1{font-size:18px;margin:0 0 4px}p.sub{color:#57606a;margin:0 0 18px}
      .user{display:flex;gap:10px;align-items:flex-start;border:1px solid #e1e4e8;border-radius:8px;padding:10px;margin:6px 0;cursor:pointer}
      .row{display:flex;gap:10px;margin-top:16px}
      button{font:inherit;font-weight:600;padding:10px 16px;border-radius:6px;border:0;cursor:pointer}
      .allow{background:#0b5fff;color:#fff}.deny{background:#eee;color:#b42318}
      code{background:#f3f4f6;padding:1px 4px;border-radius:4px}
    </style></head><body>
    <form class="card" method="post" action="{{options.Issuer}}/v1/authorize/decision">
      <h1>Dummy IdP</h1>
      <p class="sub">client <code>{{F("client_id")}}</code> &rarr; <code>{{F("redirect_uri")}}</code></p>
      {{userRadios}}
      <input type="hidden" name="client_id" value="{{F("client_id")}}">
      <input type="hidden" name="redirect_uri" value="{{F("redirect_uri")}}">
      <input type="hidden" name="state" value="{{F("state")}}">
      <input type="hidden" name="scope" value="{{F("scope")}}">
      <input type="hidden" name="nonce" value="{{F("nonce")}}">
      <input type="hidden" name="code_challenge" value="{{F("code_challenge")}}">
      <input type="hidden" name="code_challenge_method" value="{{F("code_challenge_method")}}">
      <div class="row">
        <button class="allow" type="submit" name="decision" value="allow">Authenticate</button>
        <button class="deny" type="submit" name="decision" value="deny">Deny access</button>
      </div>
    </form></body></html>
    """;

    return Results.Content(html, "text/html");
});

// ---- Authorize decision -> redirect back to the app -------------------------
app.MapPost($"{b}/v1/authorize/decision", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();
    var redirectUri = form["redirect_uri"].ToString();
    var state = form["state"].ToString();

    if (string.IsNullOrEmpty(redirectUri))
    {
        return Results.BadRequest("redirect_uri is required.");
    }

    if (form["decision"] != "allow")
    {
        return Results.Redirect(QueryHelpers.AddQueryString(redirectUri, new Dictionary<string, string?>
        {
            ["error"] = "access_denied",
            ["error_description"] = "The user denied the authentication request.",
            ["state"] = state,
        }));
    }

    var index = int.TryParse(form["userIndex"], out var i) ? i : 0;
    var user = options.Users[Math.Clamp(index, 0, options.Users.Count - 1)];

    return ApproveRedirect(
        store, user, redirectUri, state,
        form["client_id"].ToString(), form["scope"].ToString(),
        form["nonce"].ToString(), form["code_challenge"].ToString());
});

// ---- Token -----------------------------------------------------------------
app.MapPost($"{b}/v1/token", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();
    var grantType = form["grant_type"].ToString();
    var clientId = form["client_id"].ToString();

    string scope;
    DummyUser user;

    if (grantType == "authorization_code")
    {
        var pending = store.RedeemCode(form["code"].ToString());
        if (pending is null)
        {
            return TokenError("invalid_grant", "Unknown or already-used authorization code.");
        }

        if (!string.IsNullOrEmpty(clientId) && pending.ClientId != clientId)
        {
            return TokenError("invalid_grant", "client_id does not match the authorization request.");
        }

        var redirectUri = form["redirect_uri"].ToString();
        if (!string.IsNullOrEmpty(redirectUri) && pending.RedirectUri != redirectUri)
        {
            return TokenError("invalid_grant", "redirect_uri does not match the authorization request.");
        }

        var verifier = form["code_verifier"].ToString();
        if (!PkceMatches(verifier, pending.CodeChallenge))
        {
            return TokenError("invalid_grant", "PKCE code_verifier is missing or invalid.");
        }

        scope = pending.Scope;
        user = pending.User;
        clientId = string.IsNullOrEmpty(clientId) ? pending.ClientId : clientId;
    }
    else if (grantType == "refresh_token")
    {
        var session = store.RedeemRefreshToken(form["refresh_token"].ToString());
        if (session is null)
        {
            return TokenError("invalid_grant", "Unknown or already-used refresh token.");
        }

        if (!string.IsNullOrEmpty(clientId) && session.Value.ClientId != clientId)
        {
            return TokenError("invalid_grant", "client_id does not match the refresh token.");
        }

        scope = session.Value.Scope;
        user = session.Value.User;
        clientId = session.Value.ClientId;
    }
    else if (grantType == "urn:ietf:params:oauth:grant-type:token-exchange")
    {
        // RFC 8693: exchange the caller's subject_token for a token scoped to another API,
        // recording the caller as the actor (act claim) acting on the user's behalf.
        var subjectToken = form["subject_token"].ToString();
        var target = string.IsNullOrEmpty(form["audience"]) ? form["resource"].ToString() : form["audience"].ToString();

        if (string.IsNullOrEmpty(subjectToken) || string.IsNullOrEmpty(target))
        {
            return TokenError("invalid_request", "subject_token and audience (or resource) are required.");
        }

        if (!options.ExchangeAudiences.Contains(target))
        {
            return TokenError("invalid_target", $"'{target}' is not an audience this IdP will exchange for.");
        }

        var subject = await tokens.ValidateOwnAccessTokenAsync(subjectToken);
        if (subject is null)
        {
            return TokenError("invalid_grant", "subject_token is not a valid access token from this IdP.");
        }

        var actor = string.IsNullOrEmpty(clientId) ? "unknown-client" : clientId;
        scope = string.IsNullOrWhiteSpace(form["scope"]) ? "openid profile email" : form["scope"].ToString();

        Console.WriteLine($"[token-exchange] '{actor}' acting for '{subject.Sub}' -> aud '{target}'");

        return Results.Json(new
        {
            token_type = "Bearer",
            issued_token_type = "urn:ietf:params:oauth:token-type:access_token",
            expires_in = tokens.AccessTokenLifetimeSeconds,
            access_token = tokens.CreateDelegatedAccessToken(actor, target, scope, subject),
            scope,
        });
    }
    else
    {
        return TokenError("unsupported_grant_type", $"grant_type '{grantType}' is not supported.");
    }

    var accessToken = tokens.CreateAccessToken(clientId, scope, user);
    var idToken = tokens.CreateIdToken(clientId, GetNonceFromForm(form), user);
    var refreshToken = store.IssueRefreshToken(clientId, scope, user);

    Console.WriteLine($"[token] {grantType} -> issued tokens for '{user.Sub}'");

    return Results.Json(new
    {
        token_type = "Bearer",
        expires_in = tokens.AccessTokenLifetimeSeconds,
        access_token = accessToken,
        id_token = idToken,
        refresh_token = refreshToken,
        scope,
    });
});

// ---- Logout --------------------------------------------------------------------
app.MapGet($"{b}/v1/logout", (HttpRequest request) =>
{
    var post = request.Query["post_logout_redirect_uri"].ToString();
    var state = request.Query["state"].ToString();
    Console.WriteLine($"[logout] -> {post}");
    return string.IsNullOrEmpty(post)
        ? Results.Content("Signed out of the dummy IdP.", "text/plain")
        : Results.Redirect(QueryHelpers.AddQueryString(post, new Dictionary<string, string?> { ["state"] = state }));
});

Console.WriteLine($"Dummy IdP listening. Issuer: {options.Issuer}");
Console.WriteLine("DEV ONLY - do not use against anything real.");
app.Run(options.PublicUrl);

static IResult ApproveRedirect(
    SessionStore store, DummyUser user, string redirectUri, string state,
    string clientId, string scope, string nonce, string codeChallenge)
{
    var effectiveScope = string.IsNullOrWhiteSpace(scope) ? "openid profile email offline_access" : scope;

    var code = store.IssueCode(new PendingAuthorization(
        ClientId: clientId,
        RedirectUri: redirectUri,
        CodeChallenge: codeChallenge,
        Nonce: string.IsNullOrEmpty(nonce) ? null : nonce,
        Scope: effectiveScope,
        User: user,
        CreatedAt: DateTimeOffset.UtcNow));

    Console.WriteLine($"[authorize] issued code for '{user.Sub}' -> {redirectUri}");

    return Results.Redirect(QueryHelpers.AddQueryString(redirectUri, new Dictionary<string, string?>
    {
        ["code"] = code,
        ["state"] = state,
    }));
}

static string? GetNonceFromForm(IFormCollection form) =>
    string.IsNullOrEmpty(form["nonce"]) ? null : form["nonce"].ToString();

static bool PkceMatches(string codeVerifier, string codeChallenge)
{
    if (string.IsNullOrEmpty(codeVerifier) || string.IsNullOrEmpty(codeChallenge))
    {
        return false;
    }

    var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
    var computed = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    return string.Equals(computed, codeChallenge, StringComparison.Ordinal);
}

static IResult TokenError(string error, string description) =>
    Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);
