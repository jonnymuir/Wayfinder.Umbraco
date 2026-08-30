# Wayfinder.Umbraco.ReferenceApp

A real, bootable Umbraco 17 site that proves `Wayfinder.Umbraco` works end to end — the citizen
stage block, the caseworker worklist block, pickup/putback, access control — against a real
backoffice and a real content pipeline, not a mock. It sits next to the package in this repo/
solution and never ships in the NuGet package itself; it's the host for future end-to-end tests
and demo/documentation footage.

Not for production deployment — it's a transient, unattended-install dev host.

## Running it

```bash
dotnet run --project Wayfinder.Umbraco.ReferenceApp --launch-profile Wayfinder.Umbraco.ReferenceApp
```

or via the Aspire orchestrator (`Wayfinder.Umbraco.AppHost`), or the `.vscode/launch.json`
configs at the repo root ("C#: Aspire (Wayfinder.Umbraco.ReferenceApp)" / standalone).

On first boot it unattended-installs into its own local SQLite file (`umbraco/Data/`, gitignored)
and seeds three pages plus a demo blueprint — see [`ReferenceContentSeeder.cs`](ReferenceContentSeeder.cs)/
[`ReferenceBlueprintSeeder.cs`](ReferenceBlueprintSeeder.cs):

- `/` — Home, a plain landing page explaining what this app is and linking everywhere else.
- `/apply` — the citizen-facing stage block.
- `/caseworker-queue` — the caseworker-facing worklist block.

## Logins

**Backoffice** (`/umbraco`) — the real Umbraco CMS admin account, from this project's own
`appsettings.json` (`Umbraco:CMS:Unattended:UnattendedUser*`):

- Email: `admin@example.test`
- Password: `Wayfinder123!`

**Front-end demo personas** (`/demo/login`) — no password, just pick a persona. Two lanes, see
[`ReferenceAppAuth.cs`](ReferenceAppAuth.cs):

- **Alex Applicant** (citizen) — `/apply`, the citizen-facing stage block.
- **Casey Caseworker** / **Jordan Caseworker** (caseworker) — `/caseworker-queue`, the
  caseworker-facing worklist block. A citizen visiting this page is refused (page-level role
  check in `Views/referenceHome.cshtml`) rather than shown a filtered-but-real worklist —
  Wayfinder.Umbraco's own worklist block carries no access-control opinion of its own by design,
  so this reference app owns that decision itself, the way any real host would.

## MCP service-blueprint authoring

A real backoffice-authenticated MCP surface at `/wayfinder/service-blueprint-authoring/mcp`,
wired in this app's own `Program.cs` (`Wayfinder.Engine.Mcp`'s `AddServiceBlueprintAuthoringMcp()`/
`MapServiceBlueprintAuthoringMcp()` — anonymous by default, so this app chains its own
`RequireAuthorization()` onto it). Gated behind `WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin`
— the same policy the backoffice's own "Blueprints" authoring surface uses — so an MCP client needs
a real backoffice identity in the `admin` group, not an open sandbox endpoint. Confirmed live, both
ways: an admin-group identity gets a working MCP session; a non-admin one gets a real bearer token
but a `403` from the MCP endpoint itself.

Two ways to authenticate a client, below: **interactive OAuth** (recommended — the client logs
into the backoffice, tokens auto-refresh) and **client credentials** (headless/CI — a hand-minted,
non-refreshing token passed as a header).

For a full end-to-end sit-down — connect an agent, hand it a plain-language brief, watch it design
and save a branching "transfer your juggling licence" service, wire it into `/apply`, review it in
the visual editor, and run it as a citizen and a caseworker — follow
[`docs/mcp-authoring-walkthrough.md`](../docs/mcp-authoring-walkthrough.md) (the same sequence the
demo video records).

### Connecting an MCP client (interactive OAuth — recommended)

This app calls `AddWayfinderUmbracoMcpAuthentication()` (in `Program.cs`), so an MCP client
connects by logging into the Umbraco backoffice — no token minting by hand:

```bash
claude mcp add --transport http wayfinder-umbraco \
  https://localhost:44399/wayfinder/service-blueprint-authoring/mcp \
  --client-id umbraco-back-office-wayfinder-mcp \
  --callback-port 33418
claude mcp list   # a browser opens for the backoffice login, then "✔ Connected"
```

- `--client-id` is the pre-registered public (PKCE, no secret) OpenIddict client
  `WayfinderMcpOAuthClientInstaller` creates at every startup.
- `--callback-port 33418` matches the loopback redirect URI registered **for the Development
  environment only** (`WayfinderMcpOptions.LocalCallbackPorts`). Claude Code otherwise picks a
  random port, which wouldn't be a registered redirect URI. For a non-Development deployment,
  add your client's real callback URL to `Wayfinder:Mcp:RedirectUris` instead.
- The access token is your own backoffice identity, short-lived, and **refreshed automatically** —
  no re-auth every few minutes. `BlueprintsAdmin` is then checked against your actual group
  membership.

How it works: the MCP endpoint answers an unauthenticated call with `401` +
`WWW-Authenticate: … resource_metadata="…/.well-known/oauth-protected-resource"`; the client
walks that to `/.well-known/oauth-protected-resource`, then to the RFC 8414 authorization-server
metadata this package serves on Umbraco's behalf (Umbraco's backoffice OpenIddict server
publishes none), then runs the standard OAuth 2.1 Authorization Code + PKCE flow against
`/umbraco/management/api/v1/security/back-office/{authorize,token}`.

### Connecting an MCP client (headless / CI — client credentials)

For a non-interactive agent, the manual client-credentials flow still works. Three steps, once
per agent identity (steps 1–2 need a bearer token from your own interactive backoffice session —
grab it from any authenticated `/umbraco/management/api/...` request your browser makes while
logged in). The reference app also seeds one such identity at startup
(`ReferenceMcpDemoAgentSeeder`: `wayfinder-demo-agent` / `DemoAgentLocal!12345`).

1. **Create a dedicated API user**, in the `admin` group:
   ```bash
   curl -sk -X POST https://localhost:44399/umbraco/management/api/v1/user \
     -H "Authorization: Bearer <your-own-backoffice-token>" -H "Content-Type: application/json" \
     -d '{
       "email": "mcp-agent@example.test", "userName": "mcp-agent@example.test",
       "name": "MCP Agent", "kind": "Api",
       "userGroupIds": [{"id": "e5e7f6c8-7f9c-4b5b-8d5d-9e1e5a4f7e4d"}]
     }'
   ```
   (That group id is `Constants.Security.AdminGroupKey` — every default Umbraco install's built-in
   Administrators group. `"kind": "Api"` matters: only API-kind users support client credentials —
   a regular interactive user rejects the next step with `InvalidUser`.)

2. **Register client credentials for that user** (the id from step 1's `Location` header):
   ```bash
   curl -sk -X POST https://localhost:44399/umbraco/management/api/v1/user/<user-id>/client-credentials \
     -H "Authorization: Bearer <your-own-backoffice-token>" -H "Content-Type: application/json" \
     -d '{"clientId": "my-agent", "clientSecret": "<a-strong-secret>"}'
   ```
   Umbraco silently namespaces this under the hood — confirmed live by reading
   `umbracoOpenIddictApplications` directly: the row this creates has `ClientId =
   "umbraco-back-office-my-agent"`, not `"my-agent"`. The token exchange below fails with
   `invalid_client` if you forget that prefix; it isn't documented anywhere obvious.

3. **Exchange it for a bearer token, and register the MCP server**:
   ```bash
   curl -sk -X POST https://localhost:44399/umbraco/management/api/v1/security/back-office/token \
     -d grant_type=client_credentials -d client_id=umbraco-back-office-my-agent \
     -d client_secret=<a-strong-secret> -o mcp-token.json
   claude mcp add --transport http wayfinder-umbraco \
     https://localhost:44399/wayfinder/service-blueprint-authoring/mcp \
     --header "Authorization: Bearer $(jq -r .access_token mcp-token.json)"
   claude mcp list   # confirm "✔ Connected"
   ```
   This token has no refresh — re-run step 3 when it expires (`expires_in` ~1800s). Steps 1–2 are
   one-time per agent identity.

## Umbraco's generated Imaging HMAC key

On every unattended-installed boot, Umbraco writes a fresh `Umbraco:CMS:Imaging:HMACSecretKey`
into `appsettings.json`. **Don't commit it** — revert with
`git checkout Wayfinder.Umbraco.ReferenceApp/appsettings.json` before committing anything else.
