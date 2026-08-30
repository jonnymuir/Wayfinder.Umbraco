# Wayfinder.Umbraco — Project Guide for Claude Code

## What this is

`Wayfinder.Umbraco` is the Umbraco v17 host implementation of Wayfinder's GDS-style service
blueprint engine: a DB-backed, uSync-portable blueprint store, a backoffice authoring API, and
two Block Grid-composable building blocks (a citizen-facing stage journey, a caseworker-facing
worklist). It carries no multi-tenancy or auth opinion of its own — a host wires identity/tenant
resolution on top.

The engine itself, the calculation language, the rendering toolkit and the editor live in the
sibling [`jonnymuir/Wayfinder`](https://github.com/jonnymuir/Wayfinder) repo and are consumed
here as NuGet packages (`Wayfinder`, `Wayfinder.Engine`, `Wayfinder.Rendering.GovUk`,
`Wayfinder.Editor`, plus `Wayfinder.Engine.Mcp` in the reference app). nuget.org is the only
restore/publish target — there is no GitHub Packages feed.

Solo developer project. Work directly on `main` for trivial fixes; feature branches + PRs for
substantive changes.

## Projects

| Project | Purpose |
|---|---|
| `Wayfinder.Umbraco` | The publishable package — store, authoring API, the two Block Grid blocks, uSync handlers, the composer that wires it all with zero host `Program.cs`. |
| `Wayfinder.Umbraco.ReferenceApp` | A real bootable Umbraco 17 site proving the package end to end (citizen block, caseworker worklist, pickup/putback, the MCP authoring surface). Never ships. |
| `Wayfinder.Umbraco.Client` | The backoffice bundles (Blueprints tab + manifests), packed into the NuGet package via `wwwroot/dist`. |
| `Wayfinder.Umbraco.AppHost` | .NET Aspire orchestrator for the reference app. |
| `Wayfinder.Umbraco.Tests` | xUnit test suite. |

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
`Wayfinder.Umbraco.ReferenceApp/appsettings.json` on every unattended install — never commit it**
(`git checkout -- Wayfinder.Umbraco.ReferenceApp/appsettings.json`).

## Key conventions

### Testing — behavioural contracts, not implementation mirrors

Every test answers *"what should happen, observed from outside this unit?"* — never *"what does
the current code do internally?"*. Adapted from the Umbraco.Prism squad's Tester charter; the
same rules apply here.

1. **Test behaviour through public seams.** An interface method, an HTTP endpoint/response, a
   notification handler's `HandleAsync`, a rendered page. If the only way to reach the thing
   under test is `internal` + `InternalsVisibleTo`, that is the signal you are testing the wrong
   level — find the seam and assert on what it *does*.
2. **`InternalsVisibleTo` is a smell, not a tool.** This repo ships with none. If a test seems to
   need one, the production code wants a different seam, or the helper wants to be its own
   public, independently-meaningful type — fix that instead.
3. **Assert on outputs and interactions.** The argument handed to a collaborator (e.g. the
   `OpenIddictApplicationDescriptor` passed to a mocked `IOpenIddictApplicationManager`), the
   HTTP status + headers + body, the persisted row. Never reflect into private state.
4. **A behaviour-preserving refactor must not turn a test red.** Rename a private, inline a
   helper, restructure a DOM node, rename a CSS class → tests stay green. If not, the test was
   coupled to structure — rewrite it.
5. **Name tests as behaviours, one per test.**
   `OutsideDevelopment_TheClientAcceptsOnlyTheExplicitlyConfiguredHttpsCallbacks`, not
   `BuildDescriptor_ReturnsExpected`.
6. **Prefer the coarsest test that is still fast and deterministic.** A middleware test with
   `DefaultHttpContext`, a handler test with a mocked collaborator, a pure-function test for a
   genuinely pure function — all fine. Reach for `WebApplicationFactory` / a live reference-app
   run only when the behaviour *emerges from integration* (routing + auth + discovery wired
   together), and keep those few.
7. **Mock only contracts you own or the framework defines** — an interface, not a carve-out into
   a concrete class.
8. **C# stack:** xUnit + Moq + FluentAssertions. No database mocks — integration coverage uses a
   real host (the reference app / `WebApplicationFactory`).
9. **Playwright (reference-app E2E):** semantic selectors only — `getByRole`, `getByLabel`,
   `getByText`, `aria-*`, `data-*` hooks. Never CSS classes or component tag names. Wait for a
   visible loaded indicator before reading values. Date inputs: target `{fieldKey}-day` /
   `-month` / `-year`. Assert both the `[role="alert"]` summary *and* field-level errors.
10. **Keep both suites green before every PR.**

### Branch policy

Feature branches + PRs for substantive changes: `{type}/{kebab-slug}`. Direct commits to `main`
for trivial fixes only.

### Commit conventions

[Conventional Commits](https://www.conventionalcommits.org/). `feat:` = minor, `fix:`/`perf:`/
`refactor:`/`test:`/`chore:`/`docs:` = patch, `feat!:` or a `BREAKING CHANGE:` body line = major.
The version-tag release reads these.

### Code style

- No speculative abstractions — solve the problem at hand.
- Comments only where the *why* is non-obvious; match the density of the surrounding file.
- Idiomatic .NET 10 / Umbraco v17 — resolve `IContentTypeService`, `IDataTypeService`, etc. from
  DI, don't re-register.
- The package carries no auth/tenancy opinion — new host-specific behaviour goes behind a
  resolver hook or a policy the host registers, not a hardcoded assumption.
- No duplication of anything the `Wayfinder` repo already ships (C# wrappers, CSS/JS,
  govuk-frontend vendoring, calculation runtimes).
