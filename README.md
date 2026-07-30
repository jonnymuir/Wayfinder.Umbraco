# Wayfinder.Umbraco

The Umbraco-hosted implementation of [Wayfinder](https://github.com/jonnymuir/Wayfinder) —
a GDS-style service blueprint / service-design engine. This package provides:

- A DB-backed, uSync-portable service blueprint store (`prismCmsServiceBlueprint` table,
  its own `PackageMigrationPlan`).
- The generic in-process engine wiring (`AddWayfinderUmbraco()`).
- A generic business-app HTTP client (`IBusinessAppProcessManagerClient`) for hosts that
  run their own remote engine instead.
- Service-request controllers, stage/hub Razor views, and the `PrismComponents`/
  `PrismFields` partials that turn a rendered stage into GOV.UK-styled markup.
- Nonce, file-upload, and field-validation infrastructure.

It has **no multi-tenancy or auth opinion of its own** — a host wires its own identity/tenant
resolution on top. [Umbraco Prism](https://github.com/jonnymuir/Umbraco.Prism) is the
reference consumer: it layers multi-tenant OIDC and a single-queue "CMS Workflow" product
opinion on top of this package's generic primitives.

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
