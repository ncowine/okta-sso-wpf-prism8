# Okta SSO Demo — WPF + Prism 8 + DryIoc

Minimal WPF single-sign-on demo for Okta, built with **MVVM**, **Prism 8** (`Prism.DryIoc`),
and standard/official packages only:

| Concern | Package |
| --- | --- |
| OIDC flow (Authorization Code + PKCE, discovery, refresh) | Hand-rolled (`Oidc/OidcAuthorizationCodeClient`) on `HttpClient` + `Microsoft.IdentityModel.Protocols.OpenIdConnect` — no third-party OIDC client package |
| Access-token validation (JWKS signature, issuer, audience, lifetime) | `Microsoft.IdentityModel.JsonWebTokens`, `Microsoft.IdentityModel.Protocols.OpenIdConnect` |
| Encrypted token storage | `System.Security.Cryptography.ProtectedData` (Windows DPAPI) |
| Logging | `System.Diagnostics.Debug.WriteLine` |
| Config | `System.Configuration.ConfigurationManager` (`App.config` `<appSettings>`) |

## Quick start — no Okta tenant needed

```powershell
./scripts/run-demo.ps1
```

Starts the bundled **dummy IdP** + **DemoApi** + **DemoApiB**, points the app at them, and
launches it. Pick a test user in the browser (or **Deny access** to see the failure screen),
then use the Welcome screen's buttons to make token-authenticated API calls.
Full details: [`docs/dummy-idp.md`](docs/dummy-idp.md).

For a real tenant, see [Configure Okta](#configure-okta) below.

## Projects

```
SsoDemo.sln
├── src/Common.Authentication.Okta/   Reusable auth library (no WPF references)
├── src/SsoDemo.Wpf/                   WPF shell (Prism + DryIoc, MVVM)
├── tools/DummyIdp/                    Local stub OpenID Connect provider + token exchange (dev only)
├── tools/DemoApi/                     JWT-bearer-protected test API (dev only)
└── tools/DemoApiB/                    Third-party API reached two ways: direct + delegated (dev only)
```

### Reaching the third-party API (`DemoApiB`)

`DemoApiB` trusts the same IdP but expects a different audience (`api://demo-api-b`). Two paths,
both from the Welcome screen:

| | How | What `DemoApiB` sees |
| --- | --- | --- |
| **Direct** | The WPF app calls `DemoApiB` with the user's token (its `aud` includes `api://demo-api-b`). | `sub = jane.doe`, no actor — *"direct call"* |
| **Delegated** | The WPF app calls `DemoApi`, which does an **RFC 8693 token exchange** at the IdP and calls `DemoApiB` on the user's behalf. | `sub = jane.doe`, `act.sub = demo-api-a` — *"delegated (token exchange)"* |

### `Common.Authentication.Okta`

| Type | Role |
| --- | --- |
| `OktaAuthenticationOptions` | Strongly-typed config + validation |
| `IOktaAuthenticationService` / `OktaAuthenticationService` | Sign-in (interactive/silent), sign-out, holds `CurrentPrincipal` |
| `IAccessTokenValidator` / `OktaAccessTokenValidator` | Full JWT validation against tenant OIDC metadata |
| `IClaimsPrincipalFactory` / `OktaClaimsPrincipalFactory` | Maps the tenant's custom access-token claims to a `ClaimsPrincipal` |
| `ITokenStore` / `DpapiTokenStore` | DPAPI-encrypted token persistence for silent refresh |
| `Oidc/OidcAuthorizationCodeClient` | Authorization Code + PKCE flow, discovery, refresh — hand-rolled, no OIDC client package |
| `Browser/SystemBrowser` + `BrowserCallbackChannel` | `IBrowser` for `OidcAuthorizationCodeClient` using the OS default browser |
| `Platform/HkcuCustomUriSchemeRegistrar` | Registers the `app://` scheme under `HKCU` |
| `Platform/NamedPipeSingleInstanceCoordinator` | Single-instance + forwards the `app://auth/callback` redirect |

### Claims mapping

This tenant's **access token** (not the ID token) carries the identity data. The factory maps:

| Access-token claim | .NET claim | Notes |
| --- | --- | --- |
| `sub` (e.g. `first.lastName`) | `ClaimTypes.Name` | User name — required |
| `SAMAccount` | `ClaimTypes.Email` + `urn:sso-demo:sam-account` | Email / login id |
| `empID` | `urn:sso-demo:ad-emp-id` (`CustomClaimTypes.AdEmployeeId`) | AD employee id |

All other validated claims are carried through. `AuthenticationType` is set to `Okta` so
`principal.Identity.IsAuthenticated` is `true`.

## Configure Okta

1. **Applications → Create App Integration → OIDC → Native Application.**
   - Grant types: *Authorization Code*, *Refresh Token*.
   - Sign-in redirect URI: `app://auth/callback`
   - Sign-out redirect URI: `app://auth/callback`
2. **Security → API →** your custom authorization server (`default`):
   - Add the `SAMAccount` and `empID` claims to the **access token**.
   - Confirm the audience (default `api://default`).
3. Fill in `src/SsoDemo.Wpf/App.config`:

```xml
<add key="Okta:OktaDomain" value="https://your-org.okta.com" />
<add key="Okta:ClientId"   value="0oaXXXXXXXXXXXXXXXXX" />
<add key="Okta:Audience"   value="api://default" />
```

The app refuses to start while placeholder values remain.

## Run

```
dotnet run --project src/SsoDemo.Wpf
```

Sign-in is forced on launch — there is no "sign in" button:

1. **`AuthenticatingView`** ("Signing you in…") tries a silent sign-in with the stored
   refresh token (`%LOCALAPPDATA%\SsoDemo\tokens.dat`, DPAPI).
2. If that fails, it opens your default browser for Authorization Code + PKCE. Okta
   redirects to `app://auth/callback`; Windows hands it back to the running app via the
   registered scheme + a named pipe (single-instance).
3. The access token is validated and the principal is built.
   - **Success →** `WelcomeView`: "You're signed in", name / email / AD employee id, buttons to
     call the token-protected API, and a **Sign out** button.
   - **Failure →** `AccessDeniedView`: "You don't have permission to access this
     application." with an **Exit** button (the only action).
4. **Sign out** clears the local session (no browser round trip) and shows `SignedOutView`;
   its **Sign in** button restarts the flow at step 1.

### Custom-scheme registration

On first launch the app writes `HKCU\Software\Classes\app` pointing at the current
executable (no admin rights needed). To remove it:

```
reg delete HKCU\Software\Classes\app /f
```

## Logging

Everything logs through `Debug.WriteLine` — watch it in the Visual Studio **Output** window
or with **DebugView** (SysInternals). Prefixes: `[App]`, `[OktaAuthenticationService]`,
`[OktaAccessTokenValidator]`, `[OktaClaimsPrincipalFactory]`, `[OidcAuthorizationCodeClient]`,
`[SystemBrowser]`, `[BrowserCallbackChannel]`, `[SingleInstance]`, `[DpapiTokenStore]`,
`[HkcuCustomUriSchemeRegistrar]`, `[OktaOptionsFactory]`.

To also tee the same output to a file (useful when not running under a debugger), set
`SSODEMO_TRACE_FILE` before launching:

```
set SSODEMO_TRACE_FILE=%TEMP%\ssodemo.log
dotnet run --project src/SsoDemo.Wpf
```

## Requirements

- .NET SDK 8.0.x (pinned in `global.json`)
- Windows (WPF + DPAPI + registry)
