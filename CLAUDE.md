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

Solo developer project. Work directly on `main` for trivial fixes; feature branches + PRs for
substantive changes.

## Projects

| Project | Purpose |
|---|---|
| `Wayfinder.Umbraco` | The publishable package: store, authoring API, the two Block Grid blocks, uSync handlers, the composer that wires it all with zero host `Program.cs`. |
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
`Wayfinder.Umbraco.ReferenceApp/appsettings.json` on every unattended install. Never commit it**
(`git checkout -- Wayfinder.Umbraco.ReferenceApp/appsettings.json`).

## Key conventions

### Testing: behavioural contracts, not implementation mirrors

Every test answers *"what should happen, observed from outside this unit?"*, never *"what does
the current code do internally?"*. Adapted from the Umbraco.Prism squad's Tester charter; the
same rules apply here.

1. **Test behaviour through public seams.** An interface method, an HTTP endpoint/response, a
   notification handler's `HandleAsync`, a rendered page. If the only way to reach the thing
   under test is `internal` + `InternalsVisibleTo`, that is the signal you are testing the wrong
   level, so find the seam and assert on what it *does*.
2. **`InternalsVisibleTo` is a smell, not a tool.** This repo ships with none. If a test seems to
   need one, the production code wants a different seam, or the helper wants to be its own
   public, independently-meaningful type. Fix that instead.
3. **Assert on outputs and interactions.** The argument handed to a collaborator (e.g. the
   `OpenIddictApplicationDescriptor` passed to a mocked `IOpenIddictApplicationManager`), the
   HTTP status + headers + body, the persisted row. Never reflect into private state.
4. **A behaviour-preserving refactor must not turn a test red.** Rename a private, inline a
   helper, restructure a DOM node, rename a CSS class → tests stay green. If not, the test was
   coupled to structure. Rewrite it.
5. **Name tests as behaviours, one per test.**
   `OutsideDevelopment_TheClientAcceptsOnlyTheExplicitlyConfiguredHttpsCallbacks`, not
   `BuildDescriptor_ReturnsExpected`.
6. **Prefer the coarsest test that is still fast and deterministic.** A middleware test with
   `DefaultHttpContext`, a handler test with a mocked collaborator, and a pure-function test for a
   genuinely pure function are all fine. Reach for `WebApplicationFactory` / a live reference-app
   run only when the behaviour *emerges from integration* (routing + auth + discovery wired
   together), and keep those few.
7. **Mock only contracts you own or the framework defines**: an interface, not a carve-out into
   a concrete class.
8. **C# stack:** xUnit + Moq + FluentAssertions. No database mocks. Integration coverage uses a
   real host (the reference app / `WebApplicationFactory`).
9. **Playwright (reference-app E2E):** semantic selectors only: `getByRole`, `getByLabel`,
   `getByText`, `aria-*`, `data-*` hooks. Never CSS classes or component tag names. Wait for a
   visible loaded indicator before reading values. Date inputs: target `{fieldKey}-day` /
   `-month` / `-year`. Assert both the `[role="alert"]` summary *and* field-level errors.
10. **Keep both suites green before every PR.**

### Security: non-negotiable

Adapted from the Umbraco.Prism squad's Copper mandate (tenant-isolation and auth-threat
reduction). This repo has real backoffice auth, an interactive OAuth flow and discovery
documents. Security correctness is a release gate, not a follow-up, held to the same standing
as the behavioural-testing rules above.

1. **Auth, token and discovery flows are spec-exact.** OAuth 2.0 / OIDC / PKCE / RFC 8414 /
   RFC 9207 / RFC 9728 handling follows the RFC, with no fabricated issuers or identifiers, no
   "works for now" shims, no deviation for convenience. Spec vs. convenience conflict → the spec
   wins or the work stops and the trade-off is raised.
2. **Review token handling, cache boundaries, claim validation and trust chains** on every
   change that touches `Mcp/`, auth middleware, or the composer's auth wiring: opaque vs. JWT,
   audience, issuer, scope, expiry, refresh.
3. **Deny by default.** Every endpoint carries an explicit policy (`RequireAuthorization`);
   `AllowAnonymous` only with a written reason in the code. New middleware runs in a known
   position relative to `UseAuthorization`, and that position is justified where it is wired.
4. **Dev-only relaxations are gated on injected `IHostEnvironment`, never a build-time flag**,
   and must be unreachable in a deployed environment (e.g. `DisableTransportSecurityRequirement`,
   loopback HTTP redirect URIs).
5. **No secrets in source, committed config, or logs**, including the auto-written
   `Umbraco:CMS:Imaging:HMACSecretKey` (see above), connection strings and client secrets.
6. **The package keeps no auth/tenancy opinion**: cross-tenant-relevant behaviour enters via a
   resolver hook or a host-registered policy, never a hardcoded actor, queue or tenant.
7. **Ship a security regression check for any boundary you touch**: a behavioural test that
   goes red if tenant isolation, an authorization policy or a claim check regresses.
8. **Report security findings plainly.** No minimising language: "just a hack", "edge case".
   Name the defect and its impact.

### Branch policy

Feature branches + PRs for substantive changes: `{type}/{kebab-slug}`. Direct commits to `main`
for trivial fixes only.

### Commit conventions

[Conventional Commits](https://www.conventionalcommits.org/). `feat:` = minor, `fix:`/`perf:`/
`refactor:`/`test:`/`chore:`/`docs:` = patch, `feat!:` or a `BREAKING CHANGE:` body line = major.
The version-tag release reads these.

### Code style

- No speculative abstractions. Solve the problem at hand.
- Comments only where the *why* is non-obvious; match the density of the surrounding file.
- Idiomatic .NET 10 / Umbraco v17: resolve `IContentTypeService`, `IDataTypeService`, etc. from
  DI, don't re-register.
- The package carries no auth/tenancy opinion. New host-specific behaviour goes behind a
  resolver hook or a policy the host registers, not a hardcoded assumption.
- No duplication of anything the `Wayfinder` repo already ships (C# wrappers, CSS/JS,
  govuk-frontend vendoring, calculation runtimes).
