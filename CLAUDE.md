# Wayfinder.Umbraco: Project Guide for Claude Code

## What this is

`Wayfinder.Umbraco` is the Umbraco v17 host implementation of Wayfinder's service
blueprint engine: a DB-backed, uSync-portable blueprint store, a backoffice authoring API, and
two Block Grid-composable building blocks (a citizen-facing stage journey, a caseworker-facing
worklist). It carries no multi-tenancy or auth opinion of its own. A host wires identity/tenant
resolution on top.

The underlying model is the [Nielsen Norman Group service blueprint](https://www.nngroup.com/articles/service-blueprints-definition/)
(Sarah Gibbons, 2017): customer actions, frontstage, backstage, support processes, and the three
lines of separation, where NN/g's horizontal lanes are Wayfinder's `queues` (what the worklist
block renders). Journeys are delivered to GDS Service Standard practice with the real GOV.UK
Design System. Cite NN/g and GDS/GOV.UK, not other workflow products.

The engine itself, the calculation language, the rendering toolkit and the editor live in the
sibling [`jonnymuir/Wayfinder`](https://github.com/jonnymuir/Wayfinder) repo and are consumed
here as NuGet packages (`Wayfinder`, `Wayfinder.Engine`, `Wayfinder.Rendering.GovUk`,
`Wayfinder.Editor`, plus `Wayfinder.Engine.Mcp` in the reference app). nuget.org is the only
restore/publish target. There is no GitHub Packages feed.

Solo developer project, but every change reaches `main` through a pull request. `main` is
protected (required status checks, no admin bypass) and stays green at all times.

## Projects

| Project | Purpose |
|---|---|
| `Wayfinder.Umbraco` | The publishable package: store, authoring API, the two Block Grid blocks, uSync handlers, the composer that wires it all with zero host `Program.cs`. |
| `Wayfinder.Umbraco.ReferenceApp` | A real bootable Umbraco 17 site proving the package end to end (citizen block, caseworker worklist, pickup/putback, the MCP authoring surface). Never ships. |
| `Wayfinder.Umbraco.Starter` | The second publishable package, released with `Wayfinder.Umbraco` at the same version: the no-code "try it" kit (home page, sample blueprint, `/services/{key}` pages, demo caseworker, MCP endpoint wired through an `UmbracoPipelineFilter`). Development only. Never a dependency of `Wayfinder.Umbraco`. |
| `Wayfinder.Umbraco.Templates` | The third publishable package, released with `Wayfinder.Umbraco` at the same version: `dotnet new` templates. `wayfinder-agents` adds `AGENTS.md` (the short form of `docs/building-a-site.md`) and a `CLAUDE.md` that imports it. Keep the two in step. |
| `Wayfinder.Umbraco.StarterSite` | A stock `dotnet new umbraco` site plus the starter reference, nothing else. Never ships. |
| `Wayfinder.Umbraco.Starter.IntegrationTests` | Booted-host suite over `Wayfinder.Umbraco.StarterSite`, its own test process for the same one-boot-per-process reason as `Wayfinder.Umbraco.IntegrationTests`. |
| `Wayfinder.Umbraco.Client` | The backoffice bundles (Blueprints tab + manifests), packed into the NuGet package via `wwwroot/dist`. |
| `Wayfinder.Umbraco.AppHost` | .NET Aspire orchestrator for the reference app. |
| `Wayfinder.Umbraco.Tests` | xUnit unit/component suite, fast, no host boot. |
| `Wayfinder.Umbraco.IntegrationTests` | Booted-host xUnit suite: `WebApplicationFactory<Program>` over `Wayfinder.Umbraco.ReferenceApp`. Its own test process (Wayfinder's static `SupportSystemRegistry` can't be re-registered once a unit test has read it) with one shared `[Collection]` boot. Put a test here only when the behaviour genuinely emerges from a booted host (authorization enforcement, routing, the real middleware order). |

## Build and test

```bash
# Backoffice bundles must exist on disk before dotnet build/pack (packed as static web assets)
cd Wayfinder.Umbraco.Client && npm ci && npm run build && cd ..

dotnet build Wayfinder.Umbraco.slnx -c Release
dotnet test  Wayfinder.Umbraco.slnx -c Release --no-build
dotnet pack  Wayfinder.Umbraco.slnx -c Release --no-build -o ./artifacts   # validates packing

# Reference app (unattended-installs into its own SQLite on first boot)
Umbraco__CMS__Global__TimeOut=02:00:00 \
  dotnet run --project Wayfinder.Umbraco.ReferenceApp --launch-profile Wayfinder.Umbraco.ReferenceApp
# → https://localhost:44399  (backoffice admin: admin@example.test / Wayfinder123!)
```

CI (`.github/workflows/ci.yml`) runs client build → restore → build → test → pack on every push
and PR. The real publish is `package-release.yml` on a version tag, straight to nuget.org via
Trusted Publishing.

**Umbraco writes a fresh `Umbraco:CMS:Imaging:HMACSecretKey` into
`Wayfinder.Umbraco.ReferenceApp/appsettings.json` on every unattended install. Never commit it**
(`git checkout -- Wayfinder.Umbraco.ReferenceApp/appsettings.json`).

## Releasing

`Wayfinder.Umbraco`, `Wayfinder.Umbraco.Starter` and `Wayfinder.Umbraco.Templates` release
together at one version. Run `/release` (from the engineering-standards plugin):

- **Version files:** `<Version>` in `Wayfinder.Umbraco/Wayfinder.Umbraco.csproj`,
  `Wayfinder.Umbraco.Starter/Wayfinder.Umbraco.Starter.csproj` and
  `Wayfinder.Umbraco.Templates/Wayfinder.Umbraco.Templates.csproj`.
- **Also in the release PR:** the README's "Requirements and compatibility" table lists the
  Wayfinder versions the release ships with, and the changelog entry opens with the same list.
- **Pre-release checks:** `npm run check:marketplace`, `dotnet test Wayfinder.Umbraco.slnx -c Release`.
- **The `v{version}` tag** triggers `package-release.yml`, which checks the three versions match
  the tag and publishes to nuget.org through Trusted Publishing.

## Key conventions

Engineering standards (security, simplicity, testing, change flow, docs, writing style) are in
[jonnymuir/engineering-standards](https://github.com/jonnymuir/engineering-standards/blob/main/STANDARDS.md).
Follow them. This section adds only what is specific to this repo.

### Testing

- **C# stack:** xUnit + Moq + FluentAssertions. Mock only contracts you own or the framework
  defines (an interface, not a carve-out into a concrete class). This repo ships with no
  `InternalsVisibleTo`.
- `Wayfinder.Umbraco.Tests` is the fast suite. A test goes in `Wayfinder.Umbraco.IntegrationTests`
  only when the behaviour emerges from a booted host (authorization enforcement, routing, the real
  middleware order).
- **Playwright date inputs:** target `{fieldKey}-day`, `-month` and `-year`.

### Security

This repo has real backoffice auth, an interactive OAuth flow and discovery documents.

- **Review token handling, cache boundaries, claim validation and trust chains** on every change
  that touches `Mcp/`, auth middleware or the composer's auth wiring: opaque vs. JWT, audience,
  issuer, scope, expiry, refresh.
- **Every endpoint carries an explicit policy** (`RequireAuthorization`). New middleware runs in a
  known position relative to `UseAuthorization`, justified where it is wired.
- **Dev-only relaxations are gated on injected `IHostEnvironment`, never a build-time flag**, and
  are unreachable in a deployed environment (`DisableTransportSecurityRequirement`, loopback HTTP
  redirect URIs).

### Keep things small

`node scripts/check-policies.mjs` (CI) enforces the size budgets, and the build enforces the
analyzer and complexity budgets (`TreatWarningsAsErrors`, `CodeMetricsConfig.txt`). Files already
over a budget are listed in `.policy/size-baseline.json` and the baseline block at the end of
`.editorconfig`; both may only shrink.

### Umbraco version support

The package targets one Umbraco major at a time (currently 17). `Directory.Build.props` holds
`UmbracoMajor`, `UmbracoVersion` (the exact patch built and tested against), `USyncVersion` and
`OpenIddictVersion`; the `Umbraco.Cms.*` and uSync references float from that patch up to, but not
including, the next major, so a host on the next major gets a restore-time conflict rather than
v17 assemblies loading against an untested API. Moving to a new major means bumping all four
together, running both test suites and the reference app, and updating the README's requirements
table.

### Documentation

- `README.md` is the source; `MARKETPLACE.md` (the NuGet and Marketplace readme) is generated
  from it by `npm run generate:marketplace`, and CI fails if it is stale. Never edit it by hand.
- `CHANGELOG.md` is written at release time from the Conventional Commit history (see Releasing),
  so write commit subjects a package user can understand, and give a breaking change a
  `BREAKING CHANGE:` line that says how to upgrade.
- `node scripts/check-doc-links.mjs` (CI) fails on a broken relative link or heading anchor.
- README screenshots in `assets/screenshots/` come from `tests/demo/screenshots.spec.ts`
  (`npm run demo:screenshots` against a running reference app); regenerate them after a visible
  UI change.
- `evals/agent-build/` checks that the agent-facing guidance (`docs/building-a-site.md`, the
  `wayfinder-agents` template, the MCP instructions) still steers an agent to blueprints. Run it
  after changing any of them.
- Use the vocabulary in Wayfinder's
  [`docs/glossary.md`](https://github.com/jonnymuir/Wayfinder/blob/main/docs/glossary.md).

### Code style

- No speculative abstractions. Solve the problem at hand.
- Comments only where the *why* is non-obvious; match the density of the surrounding file.
- Idiomatic .NET 10 / Umbraco v17: resolve `IContentTypeService`, `IDataTypeService`, etc. from
  DI, don't re-register.
- The package carries no auth/tenancy opinion. New host-specific behaviour goes behind a
  resolver hook or a policy the host registers, not a hardcoded assumption.
- No duplication of anything the `Wayfinder` repo already ships (C# wrappers, CSS/JS,
  govuk-frontend vendoring, calculation runtimes).
