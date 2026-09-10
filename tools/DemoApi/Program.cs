using System.Security.Claims;
using DemoApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.Configure<DemoApiOptions>(builder.Configuration.GetSection("Api"));
var apiOptions = builder.Configuration.GetSection("Api").Get<DemoApiOptions>() ?? new DemoApiOptions();
builder.Services.AddHttpClient();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = apiOptions.Authority;
        options.Audience = apiOptions.Audience;
        options.RequireHttpsMetadata = apiOptions.RequireHttpsMetadata;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = apiOptions.Authority,
            ValidateAudience = true,
            ValidAudience = apiOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = "sub",
            RoleClaimType = "roles",
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                Console.WriteLine($"[auth] rejected: {ctx.Exception.Message}");
                return Task.CompletedTask;
            },
            OnTokenValidated = ctx =>
            {
                Console.WriteLine($"[auth] accepted token for '{ctx.Principal?.Identity?.Name}'");
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

var opts = app.Services.GetRequiredService<IOptions<DemoApiOptions>>().Value;

app.MapGet("/", () => Results.Content(
    $"""
    Demo API.
    Expects a bearer access token from: {opts.Authority}  (aud: {opts.Audience})
      GET /api/health   (anonymous)
      GET /api/profile  (authorized) - echoes the token identity + claims
      GET /api/orders   (authorized) - fake data keyed off the token 'sub'
      GET /api/partner  (authorized) - exchanges your token (RFC 8693) and calls DemoApiB on your behalf
    """, "text/plain"));

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

app.MapGet("/api/profile", (ClaimsPrincipal user) => Results.Ok(new
{
    authenticated = user.Identity?.IsAuthenticated ?? false,
    name = user.FindFirstValue("sub"),
    email = user.FindFirstValue("SAMAccount"),
    adEmployeeId = user.FindFirstValue("empID"),
    clientId = user.FindFirstValue("cid"),
    scopes = user.FindAll("scp").Select(c => c.Value).ToArray(),
    tokenClaims = user.Claims
        .GroupBy(c => c.Type)
        .ToDictionary(g => g.Key, g => g.Count() == 1 ? (object)g.First().Value : g.Select(c => c.Value).ToArray()),
})).RequireAuthorization();

app.MapGet("/api/orders", (ClaimsPrincipal user) =>
{
    var owner = user.FindFirstValue("sub") ?? "unknown";
    return Results.Ok(new
    {
        owner,
        retrievedAt = DateTimeOffset.UtcNow,
        orders = OrderGenerator.For(owner),
    });
}).RequireAuthorization();

// Delegated call to the third-party API: exchange the caller's token (RFC 8693) for one scoped
// to DemoApiB, with this API recorded as the actor, then call DemoApiB on the user's behalf.
app.MapGet("/api/partner", async (HttpContext ctx, IHttpClientFactory httpClientFactory) =>
{
    var incoming = ctx.Request.Headers.Authorization.ToString();
    var userToken = incoming.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? incoming[7..] : incoming;

    var http = httpClientFactory.CreateClient();

    var exchange = await http.PostAsync(opts.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
        ["subject_token"] = userToken,
        ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
        ["audience"] = opts.DownstreamAudience,
        ["client_id"] = opts.ClientId,
        ["scope"] = "openid profile email",
    }));

    var exchangeBody = await exchange.Content.ReadAsStringAsync();
    if (!exchange.IsSuccessStatusCode)
    {
        Console.WriteLine($"[partner] token exchange failed: {(int)exchange.StatusCode} {exchangeBody}");
        return Results.Json(new { error = "token_exchange_failed", detail = exchangeBody }, statusCode: 502);
    }

    var delegatedToken = System.Text.Json.JsonDocument.Parse(exchangeBody).RootElement.GetProperty("access_token").GetString();

    using var downstream = new HttpRequestMessage(HttpMethod.Get, $"{opts.DownstreamApiUrl.TrimEnd('/')}/b/resource");
    downstream.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", delegatedToken);
    var bResponse = await http.SendAsync(downstream);
    var bBody = await bResponse.Content.ReadAsStringAsync();

    Console.WriteLine($"[partner] exchanged token for '{ctx.User.FindFirstValue("sub")}' and called DemoApiB -> {(int)bResponse.StatusCode}");

    return Results.Content(
        $$"""
        {
          "note": "DemoApi called DemoApiB on your behalf using an exchanged (delegated) token.",
          "demoApiB": {{bBody}}
        }
        """,
        "application/json",
        statusCode: (int)bResponse.StatusCode);
}).RequireAuthorization();

Console.WriteLine($"Demo API listening on {opts.PublicUrl}. Trusts tokens from {opts.Authority}.");
app.Run(opts.PublicUrl);
