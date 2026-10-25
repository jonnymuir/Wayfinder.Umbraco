# A support system driven by Umbraco Automate: a walkthrough

**Configure it. Run it.** A support process (Nielsen Norman Group's third service-blueprint lane)
is added to a Wayfinder.Umbraco site with **no bespoke support-system C#**: one block of
`appsettings.json`, and an [Umbraco Automate](https://docs.umbraco.com/umbraco-automate)
automation that this reference app builds and publishes for you in code.

**What you end up with:** a "Register as a juggling coach" service. A coach applies, an NJF
registrar reviews, and the registrar's review stage runs a coaching-standards check. That check
is a configuration-only webhook support system: Wayfinder POSTs the invocation to an Automate
webhook automation on the same site; the automation runs some logic, emails the standards
officer, waits for a human accredited / provisional / referred decision, and resolves the
registrar's wait screen.

The generic mechanism lives in the `Wayfinder` repo
([`docs/guides/support-systems.md`](https://github.com/jonnymuir/Wayfinder/blob/main/docs/guides/support-systems.md),
section "Registering one from configuration alone"). Nothing Wayfinder ships knows about
Automate. Zapier, Make, n8n or a small service would satisfy the same contract.

---

## Before you start

Boot the reference app through its Aspire host, so Mailpit (a real mailbox with a web UI) and the
signing key are wired for you:

```bash
cd Wayfinder.Umbraco.Client && npm ci && npm run build && cd ..
dotnet run --project Wayfinder.Umbraco.AppHost
```

The Aspire dashboard lists two resources: **referenceapp** (`https://localhost:44399`) and
**mailpit** (`https://localhost:8025`, the mailbox UI, served with the machine's trusted
ASP.NET Core dev certificate, so `dotnet dev-certs https --trust` must have been run once).
Wait for `referenceapp` to finish its unattended install.

| | |
|---|---|
| **Backoffice login** | `admin@example.test` / `Wayfinder123!` |
| **Demo personas** | `/demo/login` on the site: **Alex Applicant** plays the coach, **Casey Caseworker** the registrar |

`dotnet run --project Wayfinder.Umbraco.ReferenceApp` directly also works. Without Mailpit the
"Send Email" step has nowhere to deliver (the run continues), and if `NJF_STANDARDS_SIGNING_KEY`
is unset the app generates an ephemeral one at startup so the signed webhook still round-trips.

---

## What is already wired (all in code and config)

- **`appsettings.json` -> `Wayfinder:SupportSystems`** declares the `njf-coaching-standards`
  support system and its `check-coaching-standards` capability: four field-ref inputs
  (`applicantName`, `yearsCoaching`, `disclosureReference`, `firstAidExpiry`), two outputs
  (`coachingStandardsOutcome`, `coachingStandardsNote`), three outcomes (`accredited`,
  `provisional`, `referred`), webhook completion. `WayfinderUmbracoComposer` calls
  `AddConfiguredSupportSystems`, so this registers on boot.
- **`service-blueprints/njf-coaching-register.yaml`** is the blueprint. Its `standards-validation`
  stage carries the `support-system-call` action; the calling gateway's routes are
  `accredited` / `provisional` / `referred`.
- **Umbraco Automate** registers through its own composer (a bare package reference plus
  `AddComposers()`); `appsettings.json` sets `Umbraco:Automate:UseNamedConnectionString` to
  `umbracoDbDSN` so it shares the CMS database.
- **`AutomateCoachingStandardsSeeder`** (a `BackgroundService`) builds and **publishes** the
  automation via `IAutomationService` on every boot: an HMAC-SHA256 webhook trigger (keyed off
  the same `Wayfinder:SupportSystems` auth config the outbound client uses, so the two always
  agree), an **If** branch on the applicant's own data, a **Send Email** to the standards
  officer, a **Request Approval** (alias `standardsOfficerDecision`), and a
  `ResolveSupportSystemOutcome` step per outcome. It also
  creates an Automate workspace if none exists, using this reference app's own admin as the
  workspace service account (a single-tenant demo shortcut).
- **`ResolveSupportSystemOutcomeAction`** is a custom Automate `[Action]` in this reference app.
  It calls `ProcessManagerEngine.ResolveSupportSystemOutcome(...)` **in process**. Automate's
  built-in HTTP Request action has non-configurable SSRF protection that blocks loopback, so an
  automation on the same box as Wayfinder cannot call the site back over HTTP. For a genuinely
  out-of-process consumer (Zapier, a remote service) the HTTP callback route
  (`MapWebhookSupportSystemCallbacks`, mapped in `Program.cs`) is the seam; this in-process
  action is the same-box equivalent.
- The webhook is always `auth.type: hmac-sha256` (`appsettings.json`). The AppHost supplies a
  per-run `NJF_STANDARDS_SIGNING_KEY`; a bare `dotnet run` generates an ephemeral one. The
  config-driven `WebhookSupportSystemClient` signs its outbound POST with that key.

There is nothing to build by hand.

---

## How the data moves

Everything the automation knows arrives in one signed POST, and everything Wayfinder learns comes
back through one resolve step.

1. **Wayfinder to Automate.** When the registrar runs the check, the `support-system-call` action
   maps blueprint fields onto the capability's inputs and POSTs this envelope to the webhook
   trigger:

   ```json
   {
     "invocationId": "…",
     "instanceId": "…",
     "supportSystemKey": "njf-coaching-standards",
     "capabilityKey": "check-coaching-standards",
     "inputs": { "applicantName": "…", "yearsCoaching": 1, "disclosureReference": "…", "firstAidExpiry": "…" }
   }
   ```

   Inside the automation that body is `trigger.body`, so every step reads it through a binding.
2. **Inside Automate.** Open the automation (**Automation** section, tree **Default > NJF Coaching
   Standards**) and select a step, then its edit button, to see its bindings:

   | Step | Reads |
   |---|---|
   | **Meets the fast-track criteria?** (If) | `${trigger.body.inputs.yearsCoaching}` >= `2` AND `${trigger.body.inputs.disclosureReference}` is not empty. True goes straight to **Resolve: accredited (auto)**. |
   | **Email the standards officer** | `${trigger.body.inputs.applicantName}` and `${trigger.body.inputs.yearsCoaching}` in the body. |
   | **Accredit, provisional, or refer?** (Request Approval) | The applicant's name and experience in its prompt. Approve means `provisional`, reject means `referred`. Its output, including the officer's **Comment**, is available to later steps as `steps.standardsOfficerDecision`. |
   | **Resolve: provisional** / **Resolve: referred** | `${trigger.body.invocationId}` to find the waiting case, and `${steps.standardsOfficerDecision.comment \| fallback:… \| json}` as the note. |

3. **Automate to Wayfinder.** The resolve step hands Wayfinder the outcome key and a result
   payload. The payload's keys are the capability's declared outputs, so they land as the case's
   `coachingStandardsOutcome` and `coachingStandardsNote` fields, which the registrar's **Confirm
   the outcome** stage and the coach's completion page display. The officer's comment therefore
   reaches both of them word for word, and a blank comment falls back to a standard note for that
   outcome. The `| json` filter encodes the comment as a JSON string, so quotes and line breaks
   in it cannot break the payload.

---

## Run the journey

1. **As the coach**: at `/demo/login` choose **Alex Applicant**, open **Apply to coach** and
   submit a name, an email, `yearsCoaching` = `1` (this forces the review path), a disclosure
   reference, and a first-aid expiry date. You land on a "we are reviewing your application"
   wait screen.
2. **As the registrar**: sign out, choose **Casey Caseworker** at `/demo/login`, open
   **Coaching register queue**, pick up the application, open **Review application**, and click
   **Run coaching-standards check**. The stage shows a waiting screen.
3. **In Mailpit**, the "needs review" email to the standards officer has arrived.
4. **In the backoffice** (`/umbraco`, signed in as `admin@example.test`), open the
   **Automation** section's **Approvals** tab. Select **Approve** on the NJF Coaching Standards
   row, type a **Comment** (for example `Lovely three-ball cascade. Book a mentored session
   before 1 March.`) and select **Approve**.
5. On the **Runs** tab, open the NJF Coaching Standards run. Every step shows **Completed**,
   ending with **Resolve Wayfinder support-system outcome**. The run view shows each step's status
   and timings, not its output; the comment shows up on the registrar's screen.
6. **Back as the registrar** (Casey), the case is still yours: the `registrar` queue is
   `team-tray`, so pickup ownership survives the send-and-wait round trip, and it shows
   **With you** rather than as a fresh pick-up. The wait screen has released into **Confirm the
   outcome**, showing `coachingStandardsOutcome` = `provisional` and, as the standards officer's
   note, the comment typed in step 4. Click **Record and notify the applicant**.
7. **As the coach** (Alex), the wait screen releases into **Application complete**, showing the
   outcome and the note.

Re-run with `yearsCoaching` = `5` and a disclosure reference to see the auto-`accredited` path
resolve with no human step (the **If** branch takes the true edge straight to a
`ResolveSupportSystemOutcome` step).

---

## What to check for security

- The invocation envelope Wayfinder POSTs carries `invocationId` but **no callback URL**. The
  in-process resolve step reads `invocationId` from the trigger body and nothing else; the HTTP
  callback route (used by out-of-process consumers) likewise takes only the `{invocationId}` path
  segment. A leaked signing key cannot redirect a resolution anywhere.
- The webhook trigger's HMAC-SHA256 authenticator rejects any POST not signed with
  `NJF_STANDARDS_SIGNING_KEY`:
  `curl -k -X POST https://localhost:44399/automate/webhook/6f1c0000-0000-0000-0000-00000000c0de -d '{}'`
  returns 401.
- `ResolveSupportSystemOutcome` marks an invocation resolved before advancing, so a second
  delivery for the same invocation is a safe no-op. The engine also rejects an outcome key
  outside `accredited/provisional/referred`.
- The out-of-process HTTP callback route (`MapWebhookSupportSystemCallbacks`) additionally
  requires the `X-Webhook-Secret` header when a callback secret is configured, and returns
  `200 {"status":"no-op"}` for an unknown or already-resolved invocation.
- The signing key is regenerated on every AppHost launch and is never committed. In a real
  deployment it would be a long-lived secret in the host's secret store.

---

## Related

- [`docs/guides/support-systems.md`](https://github.com/jonnymuir/Wayfinder/blob/main/docs/guides/support-systems.md)
  in the Wayfinder repo: the `Wayfinder:SupportSystems` schema, `AddConfiguredSupportSystems`,
  `MapWebhookSupportSystemCallbacks`, and the security model.
- [`docs/mcp-authoring-walkthrough.md`](./mcp-authoring-walkthrough.md): designing the blueprint
  itself over MCP.
