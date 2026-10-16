# Wayfinder.Umbraco.ReferenceApp

A real, bootable Umbraco 17 site that proves `Wayfinder.Umbraco` works end to end against a real
backoffice and a real content pipeline: the citizen stage block, the caseworker worklist block,
pickup and putback, access control, an Umbraco Automate-driven support system, and the MCP
authoring surface. It sits next to the package in this solution and never ships in the NuGet
package. Three other things run against it:

- `Wayfinder.Umbraco.IntegrationTests` boots it in-process with `WebApplicationFactory` for the
  behaviour that only emerges from a real host (authorization, routing, middleware order).
- `tests/perf/` holds the k6 load test the nightly performance workflow runs.
- `tests/demo/` records the walkthrough videos and the README screenshots.

It's an unattended-install dev host, not for production. Its SQLite database
(`umbraco/Data/Umbraco.sqlite.db`, gitignored) persists across restarts like any Umbraco site's;
delete `umbraco/Data/` for a clean slate.

## Running it

```bash
cd Wayfinder.Umbraco.Client && npm ci && npm run build && cd ..   # once: the backoffice bundles
dotnet run --project Wayfinder.Umbraco.ReferenceApp --launch-profile Wayfinder.Umbraco.ReferenceApp
```

Or run the Aspire orchestrator (`dotnet run --project Wayfinder.Umbraco.AppHost`), which adds
Mailpit for the Automate demo's email step and a per-run signing key. The `.vscode/launch.json`
configs at the repo root do the same ("C#: Aspire (Wayfinder.Umbraco.ReferenceApp)" / standalone).

On first boot it unattended-installs and seeds two blueprints and five pages, see
[`ReferenceContentSeeder.cs`](ReferenceContentSeeder.cs) and
[`ReferenceBlueprintSeeder.cs`](ReferenceBlueprintSeeder.cs):

| Page | Blueprint | Block |
|---|---|---|
| `/` Home | | A landing page linking everywhere else. |
| `/apply` Apply | `reference-demo` | The citizen stage block. |
| `/caseworker-queue` Caseworker queue | `reference-demo` | The caseworker worklist block. |
| `/apply-to-coach` Apply to coach | `njf-coaching-register` | The citizen stage block. |
| `/coaching-register-queue` Coaching register queue | `njf-coaching-register` | The worklist block for the registrar team. |

The coaching register's standards check is a support system run by an Umbraco Automate
automation that [`AutomateCoachingStandardsSeeder.cs`](AutomateCoachingStandardsSeeder.cs)
builds on boot; [the Automate walkthrough](../docs/automate-support-system-walkthrough.md) runs it.

## Logins

**Backoffice** (`/umbraco`), the real Umbraco admin account, from this project's own
`appsettings.json` (`Umbraco:CMS:Unattended:UnattendedUser*`):

- Email: `admin@example.test`
- Password: `Wayfinder123!`

**Front-end demo personas** (`/demo/login`): no password, just pick one. See
[`ReferenceAppAuth.cs`](ReferenceAppAuth.cs).

| Persona | Role | Use it for |
|---|---|---|
| **Alex Applicant** | citizen | Applying: `/apply` and `/apply-to-coach`. |
| **Casey Caseworker** | caseworker | Working queues: `/caseworker-queue`, and `/coaching-register-queue` as a member of the registrar team. |
| **Jordan Caseworker** | caseworker | The same as Casey; a second caseworker to show pickup ownership. |

A citizen visiting a queue page is refused (a page-level role check in
`Views/referenceHome.cshtml`) rather than shown a filtered worklist. Wayfinder.Umbraco's worklist
block carries no access-control opinion of its own by design, so this app owns that decision, the
way any real host would.

## MCP service-blueprint authoring

A real backoffice-authenticated MCP surface at `/wayfinder/service-blueprint-authoring/mcp`,
wired in this app's own `Program.cs` (the
[main README](../README.md#turning-on-mcp-authoring-in-your-own-site) walks through the same
wiring for your own site). `Wayfinder.Engine.Mcp` maps it deny-by-default; this app narrows it to
`WayfinderUmbracoAuthorizationPolicies.BlueprintsAdmin`, the same policy the backoffice Blueprints
screen uses. So an MCP client needs a real backoffice identity in the `admin` group: an
admin-group identity gets a working MCP session, and a non-admin one gets a valid bearer token but
a `403` from the MCP endpoint.

Two ways to authenticate a client, below: **interactive OAuth** (recommended, the client logs
into the backoffice, tokens auto-refresh) and **client credentials** (headless/CI, a hand-minted,
non-refreshing token passed as a header).

For a full end-to-end sit-down, connect an agent, hand it a plain-language brief, watch it design
and save a branching "transfer your juggling licence" service, wire it into `/apply`, review it in
the visual editor, and run it as a citizen and a caseworker, follow
[`docs/mcp-authoring-walkthrough.md`](../docs/mcp-authoring-walkthrough.md) (the same sequence the
demo video records).

### Connecting an MCP client (interactive OAuth: recommended)

This app calls `AddWayfinderUmbracoMcpAuthentication()` (in `Program.cs`), so an MCP client
connects by logging into the Umbraco backoffice, with no token minting by hand.

Claude Code's Node runtime doesn't read the OS trust store, so first trust the ASP.NET Core dev
certificate it will see on `https://localhost:44399` (once per machine, then `export` in each new
shell). Don't set `NODE_TLS_REJECT_UNAUTHORIZED=0`: that turns off certificate checking for every
connection the process makes.

```bash
dotnet dev-certs https --export-path ~/.aspnet/dev-cert.pem --format Pem --no-password
rm ~/.aspnet/dev-cert.key        # the export also writes the private key; Node only needs the certificate
export NODE_EXTRA_CA_CERTS=~/.aspnet/dev-cert.pem
```

Then register and log in:

```bash
claude mcp add --transport http wayfinder-umbraco \
  https://localhost:44399/wayfinder/service-blueprint-authoring/mcp \
  --client-id umbraco-back-office-wayfinder-mcp \
  --callback-port 33418
claude mcp login wayfinder-umbraco   # browser opens for the backoffice login; add --no-browser on SSH
claude mcp list                      # wayfinder-umbraco  ✔ Connected
```

`claude mcp add` only registers the server; `claude mcp login` runs the OAuth handshake. With
`--no-browser` the CLI prints the authorize URL and waits for you to paste the
`localhost:33418/callback?...` URL back.

- `--client-id` is the pre-registered public (PKCE, no secret) OpenIddict client
  `WayfinderMcpOAuthClientInstaller` creates at every startup.
- `--callback-port 33418` matches the loopback redirect URI registered **for the Development
  environment only** (`WayfinderMcpOptions.LocalCallbackPorts`). Claude Code otherwise picks a
  random port, which wouldn't be a registered redirect URI. For a non-Development deployment,
  add your client's real callback URL to `Wayfinder:Mcp:RedirectUris` instead.
- The access token is your own backoffice identity, short-lived, and **refreshed automatically**:
  no re-auth every few minutes. `BlueprintsAdmin` is then checked against your actual group
  membership.

How it works: the MCP endpoint answers an unauthenticated call with `401` +
`WWW-Authenticate: … resource_metadata="…/.well-known/oauth-protected-resource"`; the client
walks that to `/.well-known/oauth-protected-resource`, then to the RFC 8414 authorization-server
metadata this package serves on Umbraco's behalf (Umbraco's backoffice OpenIddict server
publishes none), then runs the standard OAuth 2.1 Authorization Code + PKCE flow against
`/umbraco/management/api/v1/security/back-office/{authorize,token}`.

### Connecting an MCP client (headless / CI: client credentials)

For a non-interactive agent, the manual client-credentials flow still works. It takes three
steps, once per agent identity. Steps 1 and 2 need a bearer token from your own interactive
backoffice session: copy it from any authenticated `/umbraco/management/api/...` request your
browser makes while logged in. The same `NODE_EXTRA_CA_CERTS` setup applies to step 3's
`claude mcp` calls. The reference app also seeds one such identity at startup
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
   (That group id is `Constants.Security.AdminGroupKey`, every default Umbraco install's built-in
   Administrators group. `"kind": "Api"` matters: only API-kind users support client credentials,
   a regular interactive user rejects the next step with `InvalidUser`.)

2. **Register client credentials for that user** (the id from step 1's `Location` header):
   ```bash
   curl -sk -X POST https://localhost:44399/umbraco/management/api/v1/user/<user-id>/client-credentials \
     -H "Authorization: Bearer <your-own-backoffice-token>" -H "Content-Type: application/json" \
     -d '{"clientId": "my-agent", "clientSecret": "<a-strong-secret>"}'
   ```
   Umbraco prefixes the client id: the row this creates in `umbracoOpenIddictApplications` has
   `ClientId = "umbraco-back-office-my-agent"`, not `"my-agent"`. The token exchange below fails
   with `invalid_client` if you leave the prefix off.

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
   This token doesn't refresh: re-run step 3 when it expires (`expires_in` is about 1800s).
   Steps 1 and 2 are one-time per agent identity. Don't commit `mcp-token.json`.

## Umbraco's generated Imaging HMAC key

On every unattended-installed boot, Umbraco writes a fresh `Umbraco:CMS:Imaging:HMACSecretKey`
into `appsettings.json`. **Don't commit it**: revert with
`git checkout -- Wayfinder.Umbraco.ReferenceApp/appsettings.json` before committing anything else.
