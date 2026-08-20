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

## Umbraco's generated Imaging HMAC key

On every unattended-installed boot, Umbraco writes a fresh `Umbraco:CMS:Imaging:HMACSecretKey`
into `appsettings.json`. **Don't commit it** — revert with
`git checkout Wayfinder.Umbraco.ReferenceApp/appsettings.json` before committing anything else.
