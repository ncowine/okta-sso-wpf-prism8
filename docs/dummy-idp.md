# Running the demo with the dummy IdP + demo API

Two dev-only helpers let the WPF client run end-to-end **without a real Okta tenant**:

- **`tools/DummyIdp`** — a tiny OpenID Connect provider. Issues RS256-signed tokens whose
  claims match this tenant's shape (`sub`, `SAMAccount`, `empID` in the *access* token).
- **`tools/DemoApi`** — a JWT-bearer-protected Web API. Validates the access token against the
  IdP and returns data. The app's Welcome screen has buttons to call it.

> **Dev only.** Both run over plain HTTP on localhost. Never expose them.

## One command

```powershell
./scripts/run-demo.ps1
```

Starts the dummy IdP **and** the demo API, points the WPF app at both, launches it, and stops
the services when the app exits.

## What happens

1. The WPF app starts on `AuthenticatingView`, tries a silent sign-in (no stored token yet → fails),
   then opens your **default browser** at the dummy IdP.
2. The browser shows a small page: pick **Jane Doe** / **John Smith** and click **Authenticate**,
   or click **Deny access**.
3. The IdP redirects to `app://auth/callback?code=…`; Windows hands it back to the running app.
4. **Authenticate →** `WelcomeView` ("You're signed in", `jane.doe` / `jane.doe@example.com` / `AD100234`).
   **Deny →** `AccessDeniedView`.
5. On the Welcome screen, click **GET /api/profile** or **GET /api/orders** — the access token is
   sent as a bearer header to `tools/DemoApi` and the JSON response is shown.
6. **Sign out** clears the local session and shows `SignedOutView`; **Sign in** there runs the
   flow again (pick a different user to see the API return different data).
7. Close and re-run within the token lifetime → silent sign-in via the stored refresh token,
   no browser.

The first time the browser redirects to `app://…`, Windows/your browser asks permission to open
the app — allow it (tick "always allow" to skip it next time).

## Testing API calls

`tools/DemoApi` (`http://localhost:5006`) protects its endpoints with JWT bearer auth pointed at
the dummy IdP:

**DemoApi** (`http://localhost:5006`), first-party, audience `api://default`:

| Endpoint | |
| --- | --- |
| `GET /api/health` | anonymous |
| `GET /api/profile` | `[Authorize]` — the API's view of your token (mapped identity + every claim) |
| `GET /api/orders` | `[Authorize]` — fake data seeded from the token `sub`, so each user differs |
| `GET /api/partner` | `[Authorize]` — token exchange → calls DemoApiB on your behalf (see below) |

**DemoApiB** (`http://localhost:5007`), the third-party system, audience `api://demo-api-b`:

| Endpoint | |
| --- | --- |
| `GET /b/health` | anonymous |
| `GET /b/resource` | `[Authorize]` — reports whether the caller is the user (`direct`) or a delegate (`act` claim present) |

### Two ways to reach DemoApiB

1. **Direct.** The dummy IdP issues the user's access token with `aud = ["api://default",
   "api://demo-api-b"]`, so the WPF app calls `DemoApiB` directly. `DemoApiB` sees `sub =
   jane.doe`, no actor.
2. **Delegated (RFC 8693 token exchange).** The WPF app calls `DemoApi`; `DemoApi` POSTs the
   user's token to the IdP token endpoint with
   `grant_type=urn:ietf:params:oauth:grant-type:token-exchange&audience=api://demo-api-b&client_id=demo-api-a`,
   gets back a token scoped to `api://demo-api-b` with `act = { sub: "demo-api-a" }`, and calls
   `DemoApiB` with it. `DemoApiB` sees `sub = jane.doe` **and** `act.sub = demo-api-a`.

```bash
# direct
curl -H "Authorization: Bearer <access_token>" http://localhost:5007/b/resource
# delegated
curl -H "Authorization: Bearer <access_token>" http://localhost:5006/api/partner
```

In the app, `BearerTokenHandler` calls `IOktaAuthenticationService.GetAccessTokenAsync()` (which
refreshes the token near expiry) and attaches it to every request.

Point the app elsewhere with `App.config` `Api:BaseUrl` / `Api:BaseUrlB` (or `SSO_API_BASEURL`
/ `SSO_API_BASEURL_B`). Point an API at a real Okta tenant via its `appsettings.json`
(`Api:Authority`, `Api:Audience`, `Api:RequireHttpsMetadata`); the IdP's exchangeable audiences
are `tools/DummyIdp/appsettings.json` → `DummyIdp:ExchangeAudiences`.

## Manual setup (instead of the script)

Run the four processes yourself:

```powershell
# terminal 1 - IdP
dotnet run --project tools/DummyIdp            # http://localhost:5005

# terminal 2 - DemoApi
dotnet run --project tools/DemoApi             # http://localhost:5006

# terminal 3 - DemoApiB
dotnet run --project tools/DemoApiB            # http://localhost:5007

# terminal 4 - app
$env:SSO_OKTA_DOMAIN            = "http://localhost:5005"
$env:SSO_OKTA_CLIENTID         = "sso-demo-wpf"
$env:SSO_OKTA_ALLOWINSECUREHTTP = "true"
dotnet run --project src/SsoDemo.Wpf
```

Any `Okta:Name` value in `App.config` can be overridden by the environment variable `SSO_OKTA_NAME`
(see `OktaOptionsFactory`). Or set the same values directly in `App.config` — the relevant ones:

```xml
<add key="Okta:OktaDomain" value="http://localhost:5005" />
<add key="Okta:ClientId" value="sso-demo-wpf" />
<add key="Okta:AllowInsecureHttp" value="true" />
```

## Configuring the dummy IdP

`tools/DummyIdp/appsettings.json` → `DummyIdp` section:

| Key | Meaning |
| --- | --- |
| `PublicUrl` | Scheme + host to listen on / stamp as issuer (default `http://localhost:5005`). |
| `Audience` | `aud` claim of the access token (default `api://default`). |
| `AccessTokenLifetimeMinutes` | Token lifetime (default 60). |
| `AutoApprove` | `true` → skip the sign-in page, immediately approve the first user (hands-free). |
| `Users[]` | The selectable identities: `Sub`, `SamAccount`, `EmpId`, `DisplayName`. |

Each setting is also overridable via environment (`DummyIdp__PublicUrl`, `DummyIdp__AutoApprove`, …).

## Endpoints

Base: `http://localhost:5005/oauth2/default`

| Path | |
| --- | --- |
| `/.well-known/openid-configuration` | discovery |
| `/v1/keys` | JWKS (RS256 public key) |
| `/v1/authorize` | sign-in page (or auto-approve) |
| `/v1/token` | `authorization_code` (+ PKCE S256) and `refresh_token` grants |
| `/v1/logout` | end session |
