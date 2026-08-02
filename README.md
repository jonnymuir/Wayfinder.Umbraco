# Wayfinder.Umbraco

The Umbraco-hosted implementation of [Wayfinder](https://github.com/jonnymuir/Wayfinder) —
a GDS-style service blueprint / service-design engine. This package provides:

- A DB-backed, uSync-portable service blueprint store (`wayfinderServiceBlueprint` table,
  its own `PackageMigrationPlan`).
- The generic in-process engine wiring (`AddWayfinderUmbraco()`).
- A generic business-app HTTP client (`IBusinessAppProcessManagerClient`) for hosts that
  run their own remote engine instead.
- Its own independent "Wayfinder" backoffice section — install the package, get a working
  authoring UI with zero host wiring.
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

Both are published to the same GitHub Packages feed this package itself publishes to.

## Building

Restoring requires read access to `https://nuget.pkg.github.com/jonnymuir/index.json` (see
`NuGet.config`) — set a `WAYFINDER_PACKAGES_TOKEN` environment variable to a GitHub PAT with
`read:packages` scope.

```bash
export WAYFINDER_PACKAGES_TOKEN=<your PAT>
dotnet build Wayfinder.Umbraco.slnx
dotnet pack Wayfinder.Umbraco.slnx
```

## License

MIT — see [LICENSE](LICENSE).
