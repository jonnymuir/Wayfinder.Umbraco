# A support system driven by Umbraco Automate: a walkthrough

**Configure it. Draw it. Run it.** A support process (Nielsen Norman Group's third
service-blueprint lane) is added to a Wayfinder.Umbraco site with **no bespoke C#**: one block of
`appsettings.json`, and an [Umbraco Automate](https://docs.umbraco.com/umbraco-automate)
automation drawn on the backoffice canvas that does the actual work.

**What you end up with:** a "Register as a juggling coach" service. A coach applies, an NJF
registrar reviews, and the registrar's review stage runs a coaching-standards check. That check
is a configuration-only webhook support system: Wayfinder POSTs the invocation to an Automate
webhook automation on the same site, the automation runs some logic, emails the standards
officer, waits for a human accredited / provisional / referred decision, and calls back to
release the registrar's wait screen.

The generic mechanism lives in the `Wayfinder` repo
([`docs/guides/support-systems.md`](https://github.com/jonnymuir/Wayfinder/blob/main/docs/guides/support-systems.md),
section "Registering one from configuration alone"). Nothing Wayfinder ships knows about
Automate. Zapier, Make, n8n or a small service would satisfy the same contract.

---

## Before you start

Boot the reference app through its Aspire host, so Mailpit (a real mailbox with a web UI) and the
signing secrets are wired for you:

```bash
cd Wayfinder.Umbraco.Client && npm ci && npm run build && cd ..
dotnet run --project Wayfinder.Umbraco.AppHost
```

The Aspire dashboard lists two resources: **referenceapp** (`https://localhost:44399`) and
**mailpit** (its `web` endpoint, the mailbox UI). Wait for `referenceapp` to finish its
unattended install.

You need:

| | |
|---|---|
| **Backoffice login** | `admin@example.test` / `Wayfinder123!` |
| **Demo personas** | `/demo/login` on the site: a coach (citizen) and a registrar (caseworker) |

Running `dotnet run --project Wayfinder.Umbraco.ReferenceApp` directly also works, but without
Mailpit the "Send Email" step has nowhere to deliver, and the webhook falls back to
unauthenticated loopback (the config default is `auth.type: "none"`, which logs a warning).

---

## What is already wired

- **`appsettings.json` -> `Wayfinder:SupportSystems`** declares the `njf-coaching-standards`
  support system and its `check-coaching-standards` capability: four field-ref inputs
  (`applicantName`, `yearsCoaching`, `disclosureReference`, `firstAidExpiry`), two outputs
  (`coachingStandardsOutcome`, `coachingStandardsNote`), three outcomes (`accredited`,
  `provisional`, `referred`), webhook completion. `WayfinderUmbracoComposer` calls
  `AddConfiguredSupportSystems` so this registers on boot.
- **`service-blueprints/njf-coaching-register.json`** is the blueprint. Its `standards-validation`
  stage carries the `support-system-call` action; the calling gateway's routes are
  `accredited` / `provisional` / `referred`.
- **`Program.cs`** calls `.AddUmbracoAutomate()` and maps
  `MapWebhookSupportSystemCallbacks` at `POST /wayfinder/support-systems/callbacks/{invocationId}`
  (`AllowAnonymous`, gated by the `X-Webhook-Secret` header when a callback secret is set).
- The AppHost generates a per-run HMAC signing key and callback secret and passes both to the
  reference app as `NJF_STANDARDS_SIGNING_KEY` / `NJF_STANDARDS_CALLBACK_SECRET`, and switches the
  webhook to `auth.type: hmac-sha256`.

The one thing you build by hand, once, is the automation itself.

---

## Build the automation (once)

In the backoffice (`https://localhost:44399/umbraco`), open the **Automate** section and create a
new automation. Give it the fixed id the config expects: **`6f1c0000-0000-0000-0000-00000000c0de`**
(the "Advanced" panel on the automation lets you set the id; or import
`automate/coaching-standards.automation.json` once it is committed to this repo, which carries
that id already).

**Trigger: Webhook**

- Method: `POST`.
- Authentication: **HMAC-SHA256**. Signing key: reference the config value the AppHost injects.
  In the key field, use `$NJF_STANDARDS_SIGNING_KEY` (Automate resolves a `$`-prefixed value from
  configuration). The webhook URL Automate shows you must match the one in `appsettings.json`
  (`.../umbraco/automate/webhook/6f1c0000-0000-0000-0000-00000000c0de`).

**Steps**

1. **Set Variable** `invocationId` = `${trigger.body.invocationId}` (and, for readability,
   `yearsCoaching` = `${trigger.body.inputs.yearsCoaching}`, `firstAidExpiry` =
   `${trigger.body.inputs.firstAidExpiry}`, `disclosureReference` =
   `${trigger.body.inputs.disclosureReference}`).
2. **Condition** `yearsCoaching >= 2` AND `disclosureReference` is not empty AND `firstAidExpiry`
   is in the future.
   - **True (auto path):** **HTTP Request** ->
     `POST https://localhost:44399/wayfinder/support-systems/callbacks/${invocationId}`,
     header `X-Webhook-Secret: $NJF_STANDARDS_CALLBACK_SECRET`, body
     `{ "outcomeKey": "accredited", "resultPayload": { "coachingStandardsOutcome": "accredited", "coachingStandardsNote": "Auto-accredited: two or more years' experience, a valid disclosure reference and an in-date first-aid certificate." } }`.
   - **False (review path):**
     a. **Send Email** to a fixed address, e.g. `standards-officer@example.test` (this lands in
        Mailpit), subject "Coaching register application needs review", body carrying the
        applicant name and the reason.
     b. **Request Approval**, prompt "Accredit this coach, mark provisional, or refer?", timeout
        72 hours.
        - **Approved handle -> HTTP Request** callback with `outcomeKey: "provisional"` and a note.
        - **Rejected handle -> HTTP Request** callback with `outcomeKey: "referred"` and a note.

**Publish** the automation (the webhook endpoint returns 409 until it is published).

The applicant-facing email is sent by Wayfinder itself once the outcome resolves, not by
Automate, so the automation never emails an address taken from the request body.

Then export it (**... -> Export**) and commit the JSON as
`Wayfinder.Umbraco.ReferenceApp/automate/coaching-standards.automation.json` so the next person
can import it in one step.

---

## Run the journey

1. **As the coach** (`/demo/login`), open **Apply to coach** and submit: a name, an email,
   `yearsCoaching` = `1` (this forces the review path), a disclosure reference, and a first-aid
   expiry date. You land on a "we are reviewing your application" wait screen.
2. **As the registrar**, open **Coaching register queue**, pick up the application, open **Review
   application**, and click **Run coaching-standards check**. The stage shows a waiting screen.
3. **In Mailpit**, the "needs review" email to the standards officer has arrived.
4. **In the backoffice Automate section**, open **Pending approvals** and approve. The automation's
   run history shows the **HTTP Request** step posting the callback.
5. **Back as the registrar**, the wait screen releases into **Confirm the outcome**, showing
   `coachingStandardsOutcome` = `provisional` and the standards officer's note. Click **Record and
   notify the applicant**.
6. **As the coach**, the wait screen releases into **Application complete**, showing the outcome
   and the note.

Re-run with `yearsCoaching` = `5` and a future first-aid date to see the auto-`accredited` path
resolve without a human step.

---

## What to check for security

- The invocation envelope Wayfinder POSTs carries `invocationId` but **no callback URL**. The
  automation's HTTP Request step targets a fixed URL you typed; only the `{invocationId}` path
  segment comes from the trigger body. A leaked signing key therefore cannot redirect the
  callback anywhere.
- The webhook trigger's HMAC-SHA256 authenticator rejects any POST not signed with
  `NJF_STANDARDS_SIGNING_KEY`. Try `curl -X POST .../umbraco/automate/webhook/6f1c...c0de -d '{}'`
  and see a 401.
- The callback endpoint rejects a missing or wrong `X-Webhook-Secret` with 401. A replayed
  callback for an already-resolved invocation returns `200 {"status":"no-op"}` and does not
  advance the cursor a second time. An outcome key outside `accredited/provisional/referred`
  returns 400.
- The signing key and callback secret are regenerated on every AppHost launch and are never
  committed. In a real deployment they would be long-lived secrets in the host's secret store.

---

## Related

- [`docs/guides/support-systems.md`](https://github.com/jonnymuir/Wayfinder/blob/main/docs/guides/support-systems.md)
  in the Wayfinder repo: the `Wayfinder:SupportSystems` schema, `AddConfiguredSupportSystems`,
  `MapWebhookSupportSystemCallbacks`, and the security model.
- [`docs/mcp-authoring-walkthrough.md`](./mcp-authoring-walkthrough.md): designing the blueprint
  itself over MCP.
