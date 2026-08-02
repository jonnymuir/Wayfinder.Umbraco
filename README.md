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
  its own `PackageMigrationPlan`).
- The generic in-process engine wiring (`AddWayfinderUmbraco()`).
- A generic business-app HTTP client (`IBusinessAppProcessManagerClient`) for hosts that
  run their own remote engine instead.
- Its own independent "Blueprints" backoffice section — install the package, get a working
  authoring UI with zero host wiring: `WayfinderSectionAccessSeeder` grants it to the built-in
  Administrators group automatically on first boot (configurable via
  `WayfinderServiceDesignOptions.AdminGroupAliases`), and the authoring API itself enforces the
  same group list (`WayfinderAdminHandler`) — not just nav-visibility — so a backoffice user
  outside that list can't reach it either way.
- Service-request controllers, stage/hub Razor views, and a built-in GOV.UK-styled
  component/field catalog (`Views/Partials/_WayfinderComponents`/`_WayfinderFields`) — a host
  overrides any single type by placing a same-named partial at `Views/Partials/Components`/
  `Fields` in its own app; see `ComponentPartialResolver`'s own remarks for exactly how that
  precedence works and why it's implemented explicitly rather than inherited from ASP.NET
  Core's own RCL view resolution.
- Nonce, file-upload, and field-validation infrastructure.

It has **no multi-tenancy or auth opinion of its own** — a host wires its own identity/tenant
resolution on top. [Umbraco Prism](https://github.com/jonnymuir/Umbraco.Prism) is the
reference consumer: its `UmbracoPrism.TestSite` installs this package directly and owns its
own small demo-queue implementation (identity resolution, a single-queue constraint via this
package's own `SingleQueueStructuralValidator`) — `UmbracoPrism.Core` itself carries no
service-design opinion at all.

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
