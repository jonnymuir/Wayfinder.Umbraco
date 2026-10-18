# Changelog

Notable changes to the `Wayfinder.Umbraco` package. This project follows
[semantic versioning](https://semver.org/). Each release also names the Wayfinder package
versions it depends on; see [Requirements and compatibility](README.md#requirements-and-compatibility).

Releases before 3.0.0 are listed on
[GitHub Releases](https://github.com/jonnymuir/Wayfinder.Umbraco/releases).

## [Unreleased]

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
