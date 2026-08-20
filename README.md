<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/wordmark-umbraco-dark.png">
  <img src="assets/wordmark-umbraco-light.png" alt="Wayfinder for Umbraco" height="70">
</picture>

[![CI](https://github.com/jonnymuir/Wayfinder.Umbraco/actions/workflows/ci.yml/badge.svg)](https://github.com/jonnymuir/Wayfinder.Umbraco/actions/workflows/ci.yml)
[![Wayfinder.Umbraco](https://img.shields.io/nuget/v/Wayfinder.Umbraco.svg?label=Wayfinder.Umbraco)](https://www.nuget.org/packages/Wayfinder.Umbraco)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

The Umbraco-hosted implementation of [Wayfinder](https://github.com/jonnymuir/Wayfinder) —
a GDS-style service blueprint / service-design engine. This package provides:

- A DB-backed, uSync-portable service blueprint store (`wayfinderServiceBlueprint` table,
  its own `PackageMigrationPlan`), and an authoritative, in-process engine (`AddWayfinderUmbraco()`)
  — no remote "business app" to proxy to; the engine runs where Umbraco runs.
- Two Block Grid-composable building blocks, shipped via the same migration plan (created on
  install, no manual uSync import required) — a CMS editor drags either onto any content page,
  the same way they'd compose any other page: `wayfinderServiceRequestStage` (the citizen-facing
  journey — GetCurrent/Advance, GOV.UK-styled components, file upload) and
  `wayfinderServiceRequestWorklist` (the caseworker-facing queue — pickup/putback, filtering,
  paging; renders a picked item inline via the same stage-rendering pipeline the stage block
  itself uses).
- Real multi-queue support — a blueprint can declare as many queues as it needs; each block only
  ever renders what the signed-in actor's own `ActorProfile` can see.
- A "Blueprints" entry under Umbraco's own built-in **Settings** section (Advanced group) —
  install the package, get a working authoring UI with zero host wiring. No custom section, and
  nothing to grant on startup: Settings is a section every default install already grants to
  Administrators, so nav visibility just falls out of Umbraco's own permissions. The authoring
  API enforces its own separate boundary on top (`WayfinderAdminHandler`, configurable via
  `WayfinderServiceDesignOptions.AdminGroupAliases`) — an authenticated backoffice user without
  Settings access can't reach it either way, not just hidden from the nav.
- A built-in GOV.UK-styled component/field catalog (`Views/Partials/_WayfinderComponents`/
  `_WayfinderFields`) — a host overrides any single type by placing a same-named partial at
  `Views/Partials/Components`/`Fields` in its own app; see `ComponentPartialResolver`'s own
  remarks for exactly how that precedence works and why it's implemented explicitly rather than
  inherited from ASP.NET Core's own RCL view resolution.
- Nonce, file-upload, field-validation, and live workflow-state polling infrastructure (a
  waiting/join-gateway stage's own page polls in place rather than needing a manual refresh —
  see below for the one thing a host must wire up for that to work).

It has **no multi-tenancy or auth opinion of its own** — a host wires its own identity/tenant
resolution on top via `WayfinderServiceDesignOptions.ResolveTenantId`/`ResolveUserId`/
`ResolveAccessProfile` (all required). One more policy a host must register itself: the polling
endpoint (`ServiceRequestPollController`) is gated behind the
`WayfinderUmbracoAuthorizationPolicies.ServiceRequestPolling` named policy — register it against
whatever authentication scheme the host actually uses (see
`Wayfinder.Umbraco.ReferenceApp/ReferenceAppComposer.cs` for a minimal working example). Missing
it isn't loud: the waiting screen's own poll script just gets denied requests, retries forever,
and the page never live-updates — a manual refresh still works either way, which is exactly what
makes it easy to miss.

[Umbraco Prism](https://github.com/jonnymuir/Umbraco.Prism) is the reference consumer for
multi-tenancy/branding — `UmbracoPrism.Core` itself carries no service-design opinion at all;
only its `UmbracoPrism.TestSite` installs this package directly.

## Reference app

[`Wayfinder.Umbraco.ReferenceApp`](Wayfinder.Umbraco.ReferenceApp/README.md) — a real, bootable
Umbraco 17 site proving this package end to end (backoffice + demo logins documented there).

## Depends on

- [`Wayfinder`](https://github.com/jonnymuir/Wayfinder) — domain models, calculation engine
- [`Wayfinder.Engine`](https://github.com/jonnymuir/Wayfinder) — the state-machine engine

Both are published to nuget.org, the only source this package restores or publishes against.

## Building

```bash
dotnet build Wayfinder.Umbraco.slnx
dotnet pack Wayfinder.Umbraco.slnx
```

## License

MIT — see [LICENSE](LICENSE).
