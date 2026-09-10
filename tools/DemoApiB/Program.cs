using System.Security.Claims;
using System.Text.Json;
using DemoApiB;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.Configure<DemoApiBOptions>(builder.Configuration.GetSection("Api"));
var apiOptions = builder.Configuration.GetSection("Api").Get<DemoApiBOptions>() ?? new DemoApiBOptions();

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
                var act = ActorOf(ctx.Principal);
                Console.WriteLine(act is null
                    ? $"[auth] accepted direct call from '{ctx.Principal?.Identity?.Name}'"
                    : $"[auth] accepted delegated call: '{act}' on behalf of '{ctx.Principal?.Identity?.Name}'");
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

var opts = app.Services.GetRequiredService<IOptions<DemoApiBOptions>>().Value;

app.MapGet("/", () => Results.Content(
    $"""
    Demo API B — a third-party system.
    Trusts tokens from: {opts.Authority}   (aud: {opts.Audience})
    Two ways in:
      1. direct    — the client calls with the user's token (aud includes {opts.Audience})
      2. delegated — DemoApi exchanges the user's token (RFC 8693) and calls with 'act' = demo-api-a
      GET /b/health    (anonymous)
      GET /b/resource  (authorized)
    """, "text/plain"));

app.MapGet("/b/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

app.MapGet("/b/resource", (ClaimsPrincipal user) =>
{
    var subject = user.FindFirstValue("sub") ?? "unknown";
    var actor = ActorOf(user);

    return Results.Ok(new
    {
        resource = "Partner ledger",
        subject,
        calledVia = actor is null ? "direct" : "delegated (token exchange)",
        actor,
        audience = user.FindAll("aud").Select(c => c.Value).ToArray(),
        retrievedAt = DateTimeOffset.UtcNow,
        entries = PartnerLedger.For(subject),
    });
}).RequireAuthorization();

Console.WriteLine($"Demo API B listening on {opts.PublicUrl}. Trusts tokens from {opts.Authority} (aud {opts.Audience}).");
app.Run(opts.PublicUrl);

// The RFC 8693 'act' claim, when present, is {"sub": "<actor client>"}.
static string? ActorOf(ClaimsPrincipal? user)
{
    var act = user?.FindFirst("act")?.Value;
    if (string.IsNullOrEmpty(act))
    {
        return null;
    }

    try
    {
        using var doc = JsonDocument.Parse(act);
        return doc.RootElement.TryGetProperty("sub", out var sub) ? sub.GetString() : act;
    }
    catch (JsonException)
    {
        return act;
    }
}
