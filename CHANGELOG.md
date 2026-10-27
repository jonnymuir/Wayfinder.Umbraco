# Changelog

Notable changes to the `Wayfinder.Umbraco` package. This project follows
[semantic versioning](https://semver.org/). Each release also names the Wayfinder package
versions it depends on; see [Requirements and compatibility](README.md#requirements-and-compatibility).

Releases before 3.0.0 are listed on
[GitHub Releases](https://github.com/jonnymuir/Wayfinder.Umbraco/releases).

## [Unreleased]

### Fixed

- The **GitHub Copilot** tab's `~/.copilot/mcp-config.json` entry uses the top-level `oauthClientId`
  key, so Copilot CLI no longer warns that the nested `oauth.clientId` key is deprecated.

## [6.2.0] — 2026-10-27

Depends on Wayfinder 0.25.0, Wayfinder.Editor 0.11.0, Wayfinder.Engine.Worklist 0.5.0 and
Wayfinder.Rendering.GovUk 0.12.0. `Wayfinder.Umbraco.Starter` 6.2.0 also depends on
Wayfinder.Engine.Mcp 0.25.0.

### Fixed

- GitHub Copilot CLI, and any other MCP client that listens on a port the operating system picks,
  can now sign in to the design partner endpoint. It was refused with "the specified redirect_uri
  is not valid for this client application". A registered loopback IP callback (`127.0.0.1` or
  `[::1]`) now accepts any port, as RFC 8252 section 7.3 requires; everything else about the
  callback must still match exactly.

### Added

- A **GitHub Copilot** tab in **Connect a design partner**, with the `~/.copilot/mcp-config.json`
  entry for this site.

## [6.1.0] — 2026-10-26

Depends on Wayfinder 0.25.0, Wayfinder.Editor 0.11.0, Wayfinder.Engine.Worklist 0.5.0 and
Wayfinder.Rendering.GovUk 0.12.0. `Wayfinder.Umbraco.Starter` 6.1.0 also depends on
Wayfinder.Engine.Mcp 0.25.0.

### Added

- `Wayfinder.Umbraco.Starter`, a new package for trying Wayfinder on a fresh Umbraco site with no
  code. It adds a home page, a sample *Request a callback* service with a public queue and a staff
  queue, a page for every saved blueprint at `/services/{key}`, a one-click demo caseworker with a
  work queue, and the MCP design partner endpoint with backoffice sign-in. Access comes from the
  blueprints themselves: a blueprint's first queue is public and every other queue is staff work,
  so a service designed in conversation works as soon as it is saved. Development only: it refuses
  to start in any other environment unless `Wayfinder:Starter:AllowOutsideDevelopment` is set. It
  is released with `Wayfinder.Umbraco`, at the same version. See
  [Try it in five minutes](README.md#try-it-in-five-minutes).

## [6.0.0] — 2026-10-25

Depends on Wayfinder 0.25.0, Wayfinder.Editor 0.11.0, Wayfinder.Engine.Worklist 0.5.0 and
Wayfinder.Rendering.GovUk 0.12.0.

### Changed (breaking)

- A service blueprint is YAML everywhere this package stores or carries one: the
  `wayfinderServiceBlueprint` table's `Yaml` column (was `Json`), uSync export files, and the
  backoffice authoring API, where a whole blueprint is an `application/yaml` body in both
  directions (read, validate, save) and the simulate request carries it as a YAML string. Lists,
  catalogs and outcomes are still JSON. The `blueprints-as-yaml` package migration renames the
  column and rewrites each stored blueprint as YAML; one that can't be read is logged and left as
  it was, to fix or reseed. Seed files for a host are `*.yaml`.
- To upgrade, update the package and start the site so the migration runs. A host that seeds
  blueprints from files converts them to YAML (JSON is valid YAML, so
  `ServiceBlueprintYaml.Deserialize` reads one and `ServiceBlueprintYaml.Serialize` writes it) and
  reads them with `ServiceBlueprintYaml`.

## [5.2.2] — 2026-10-24

Depends on Wayfinder 0.24.0, Wayfinder.Editor 0.10.2 and Wayfinder.Rendering.GovUk 0.11.0.

### Changed

- In the Blueprints editor, the notes under the Properties panel fold away. They start closed, the
  heading says how many notes are open on what is selected, and once opened they stay open as the
  selection changes.

## [5.2.1] — 2026-10-24

Depends on Wayfinder 0.24.0, Wayfinder.Editor 0.10.1 and Wayfinder.Rendering.GovUk 0.11.0.

### Fixed

- In the Blueprints editor, the box for adding a design note to the selected stage, gateway or route
  is pinned under the Properties panel. It was below the panel's visible area and could not be
  reached.

## [5.2.0] — 2026-10-24

Depends on Wayfinder 0.24.0, Wayfinder.Editor 0.10.0 and Wayfinder.Rendering.GovUk 0.11.0.

### Added

- **Connect a design partner** on the Blueprints screen: setup for Claude Code, Claude Desktop, VS
  Code and any other MCP client, with this site's MCP server URL, OAuth client ID and callback port
  filled in, copy buttons, downloadable `.mcp.json` and `.vscode/mcp.json` files, and starter
  prompts for designing a new service and for working through a blueprint's design notes. It reads
  a new `GET /umbraco/management/api/v1/wayfinder/design-partner` route (`BlueprintsAdmin`), which
  also says which sign-in callbacks each client still needs on this site.

### Changed

- In Development, the MCP OAuth client also accepts `http://127.0.0.1:{port}/` for each
  `Wayfinder:Mcp:LocalCallbackPorts` entry, the callback VS Code signs in through, so VS Code
  connects to a local site with no configuration.

## [5.1.0] — 2026-10-23

Depends on Wayfinder 0.24.0, Wayfinder.Editor 0.10.0 and Wayfinder.Rendering.GovUk 0.11.0.

### Added

- Design notes in the Blueprints editor: pin feedback to a stage, gateway or route, see a marker on
  the canvas, and reply, resolve or reopen in the new Notes tab. An AI design partner connected over
  MCP works through them with Wayfinder's `list_design_notes`, `add_design_note`,
  `resolve_design_note` and `reply_to_design_note` tools. Notes are kept in a new
  `wayfinderDesignNote` table (`UmbracoDesignNoteStore`), not in the blueprint and not in uSync, and
  are deleted with their blueprint. The management API gains
  `service-blueprints/{definitionKey}/notes` routes under the `BlueprintsAdmin` policy, and a note
  made there is attributed to the signed-in backoffice user.

### Changed

- The Blueprints editor follows changes saved elsewhere: when an agent saves over MCP while a
  designer has the blueprint open with nothing unsaved, the canvas moves to the new version and
  marks what changed (from Wayfinder.Editor 0.10.0).

## [5.0.0] — 2026-10-19

Depends on Wayfinder 0.23.0 and Wayfinder.Rendering.GovUk 0.11.0.

### Added

- `AddWayfinderVisitors`: anonymous visitors with a way back. A visitor is a random secret in an
  HttpOnly cookie, and their requests record a one-way hash of it, never the secret. A private link in
  an email (`/wayfinder/link`, 30 days) brings them back on any device: following it asks "Continue in
  this browser?" first, and only that answer adds the browser, with a secret of its own. Signing in
  moves what a visitor started onto their account; a team member never takes over a visitor's
  requests. New tables `wayfinderVisitorDevice` and `wayfinderPrivateLink`.
- `AddWayfinderEmails`: Razor-template emails sent as requests move on, through Umbraco's email
  sender, in the background. A conversation is quoted as it was at the move, and queued jobs hold ids
  only. `IWayfinderEmailQueue` lets a host queue them durably (Hangfire, say).
- `AddWayfinderRecaptcha`: a Google reCAPTCHA v3 `IStageSubmissionGuard` that fails closed.
- `LatestRequestPolicy`: opening a blueprint's page picks up the person's latest request whatever its
  state, for conversations such as a contact page.
- `Wayfinder:DisplayTimeZone` and `WayfinderDisplayTime`: conversation times and hub dates in your
  visitors' time zone.
- The worklist block renders through `_WayfinderWorklist.cshtml` and `_WayfinderWorklistItem.cshtml`,
  which a host can replace, with each item's request field values in the model, and the whole
  worklist through `_WayfinderWorklistForPage.cshtml`, which a host can render from its own view.
- After a caseworker's move hands a request on, the worklist says it's done, not that the item is no
  longer in their queue.
- The reference app has a contact-us example open to anonymous visitors, with emails written to
  `umbraco/Data/Mail`.

### Fixed

- A failed submission's problems and typed values are shown only on the form of the request they came
  from. They rode in TempData with no owner, so a caseworker's reply (an "access denied" and the reply
  text) turned up on the next stage form the same browser opened, such as a visitor's conversation.
- A caseworker's reply that hands a request back is no longer treated as a failed submission
  (Wayfinder 0.23.0's `moved_on`).

### Changed

- **Breaking:** `ServiceRequestStageService.RenderCurrentAsync` takes a `StageFlash` in place of
  `problems` and `formValues`, and `ServiceRequestStageAdvanceResult` carries the request's
  `InstanceId`. A host rendering stages itself passes `StageFlash.Take(TempData)`, and writes a failed
  submission's `result.Flash?.Keep(TempData)`.
- The reference app identifies anonymous citizens as visitors instead of one shared `"anonymous"` id.

## [4.1.0] — 2026-10-18

### Added

- `IStageSubmissionGuard`, for bot protection or any other check a stage submission must pass. Each
  registered guard's fields are added to every stage form, and its check runs on every submission once
  the nonce is verified and before any field, file or engine work. A rejection changes nothing and
  returns the person to the stage with the guard's message. With none registered, nothing changes.
- `ServiceRequestTransitionedNotification`, an Umbraco notification published once per saved move
  (a submitted stage, a caseworker's action, a gateway, a support system's outcome), so a host can react
  to one, sending an email when a reply reaches the citizen, say. It is raised from the transitions the
  engine records, which is the only reliable signal: a caseworker's move into a queue they cannot see
  saves, but their own response reads as access denied.
- `_WayfinderStageForBlueprint`, the view the stage block renders through, so a host can render a
  journey from its own block or template: `Html.PartialAsync("~/Views/Partials/_WayfinderStageForBlueprint.cshtml", "blueprint-key")`.

### Changed

- Requires Wayfinder 0.22.0 (Rendering.GovUk 0.10.0, Editor 0.9.0). Brings the `conversation`
  component, which renders here with no extra wiring, and the engine fix that clears a pickup when a
  plain stage-to-stage hop moves work into a different queue: a request a caseworker sends back to the
  citizen now stays visible to the citizen.

### Security

- The stage form's hidden fields are attribute-encoded. `ReturnUrl` is the page's own path, which the
  person requesting it controls, and was written into the form unencoded: a page with the stage block
  reachable at a path containing a quote would have allowed script injection. No such route was found,
  but the values are now encoded regardless.

### Fixed

- Keeps an anonymous visitor's request once it has been handed over to another queue. Before, the
  database store gave every anonymous request a thirty-minute sliding expiry, so a submission waiting
  in a team's worklist (a contact message, say) was deleted within the hour unless someone opened it.
  Now only a draft still in the visitor's own queue expires with their session. Handed-over requests,
  and the personal details in them, are kept like a signed-in member's, so check your retention
  policy covers them. Requires Wayfinder 0.21.0.

## [4.0.0] — 2026-10-16

### Breaking changes

- `UmbracoProcessManagerEngine`'s constructor takes `IAuditLogStore` and the registered
  `IRequestConcurrencyPolicy`s, and takes an optional `IServiceInputsResolver` instead of a
  resolver delegate. A host resolves the engine from DI and never constructs it.

  **To upgrade**, a host that built the engine by hand to pass a `serviceInputsResolver`
  registers the resolver instead and deletes its engine construction:

  ```csharp
  // 3.x
  builder.Services.AddSingleton(sp => new UmbracoProcessManagerEngine(..., serviceInputsResolver: MyInputs.Resolve));
  // 4.0
  builder.Services.AddSingleton<IServiceInputsResolver, MyInputs>();
  ```

  A resolver no longer needs to copy stored values back for the fields it doesn't supply.
  Wayfinder 0.20.0 keeps each `source: "service"` field's stored value unless the resolver
  supplies one.

### Fixed

- **Blueprints and requests could not be saved on SQL Server.** Their JSON columns were created
  as `NVARCHAR(255)`, so any real blueprint failed with "String or binary data would be
  truncated". They are now `NVARCHAR(MAX)`, and a migration widens existing tables on upgrade.
  SQLite was not affected.
- The audit log store and concurrency policies a host registered were never passed to the
  engine. A registered `IRequestConcurrencyPolicy` now decides whether a request starts.

### Added

- Audit events are kept in the database (`wayfinderAuditEvent`) instead of in memory, where they
  were lost on restart and grew without limit. Events older than `Wayfinder:AuditRetention` are
  removed; the default is 365 days, and `00:00:00` keeps them forever.
- CI runs the integration suite against SQL Server as well as SQLite.

### Documentation

- The README quickstart compiles against 3.0.0 and keeps the safe `NameIdentifier` user id default.
- A "Turning on MCP authoring in your own site" section, a requirements and compatibility table,
  and a rewritten "Security responsibilities" section that matches what the package serves.
- Connecting Claude Code over HTTPS now trusts the dev certificate with `NODE_EXTRA_CA_CERTS`
  instead of turning certificate checks off.

### Dependencies

`Wayfinder` and `Wayfinder.Engine` 0.20.0, `Wayfinder.Rendering.GovUk` 0.9.0, `Wayfinder.Editor`
0.8.0, `Wayfinder.Engine.Worklist` 0.4.0. Umbraco 17.7.0 or later.

## [3.0.0] — 2026-10-12

### Breaking changes

- `WayfinderServiceDesignOptions.ResolveAccessProfile` is now asynchronous:
  `Func<HttpContext, string?, Task<ActorProfile>>`. The package awaits it on every stage render,
  form post, worklist action, poll and stage data request.

  **To upgrade**, return a task. A resolver with nothing to await wraps its result:

  ```csharp
  // 2.x
  options.ResolveAccessProfile = (ctx, blueprintKey) => MyQueues.ProfileFor(ctx.User);
  // 3.0
  options.ResolveAccessProfile = (ctx, blueprintKey) => Task.FromResult(MyQueues.ProfileFor(ctx.User));
  ```

  A resolver that looked things up with `.GetAwaiter().GetResult()` should now `await` instead.
  It runs on every request, so cache what it looks up.
- The worklist service methods are now `GetWorklistAsync`, `PickupAsync` and `PutbackAsync`.

### Performance

- `UmbracoServiceBlueprintStore` serves `ListAsync` and `LoadAsync` from an in-memory snapshot.
  Saves and deletes through the store take effect on the next read; writes that bypass it (a uSync
  import, another server) appear within 30 seconds. Hosts that need a blueprint's queues use the
  new `IBlueprintQueueDirectory` instead of loading every blueprint.

### Dependencies

`Wayfinder` and `Wayfinder.Engine` 0.19.0, `Wayfinder.Rendering.GovUk` 0.9.0, `Wayfinder.Editor`
0.8.0, `Wayfinder.Engine.Worklist` 0.4.0. Umbraco 17.7.0 or later.
