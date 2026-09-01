# Design a service over MCP: a walkthrough

**Design it. See it. Run it.** A service designer describes a problem in plain language, an AI
design partner turns it into a working service blueprint through Wayfinder's authoring tools, and
you publish it as a page in Umbraco and run it end to end.

This is the exact sequence the demo video (`tests/demo/mcp-authoring-demo.spec.ts`) records,
written so you can do it yourself against a local `Wayfinder.Umbraco.ReferenceApp`. Every command
and click below is what the recording performs.

**What you end up with:** a complete branching "transfer your juggling licence" service, designed
from a single brief, live on the site's `/apply` page, reviewable in the visual editor, and
running for a real applicant and a real caseworker.

---

## Before you start

Boot the reference app from a fresh database, so the run starts from the seeded `reference-demo`
placeholder and Act 1 starts from a clean OAuth client entry:

```bash
rm -rf Wayfinder.Umbraco.ReferenceApp/umbraco/Data/*
Umbraco__CMS__Global__TimeOut=02:00:00 \
  dotnet run --project Wayfinder.Umbraco.ReferenceApp --launch-profile Wayfinder.Umbraco.ReferenceApp
```

Wait for it to finish its unattended install and serve `https://localhost:44399`. The
`Umbraco__CMS__Global__TimeOut` is not optional for a real sit-down: Act 2's design conversation
can run past Umbraco's default backoffice session timeout, and Act 3 reuses your Act 1 login.

You need:

| | |
|---|---|
| **Backoffice login** | `admin@example.test` / `Wayfinder123!` |
| **Claude Code** | signed in, `claude` on your `PATH`, version 2.1.x or later (for `--client-id` / `--callback-port` on `claude mcp add`) |
| **A terminal** | for Acts 1 and 2 |
| **A one-page PDF** | any real PDF, for the Act 5 uploads |

---

## Act 1: connect the design partner

The design partner connects by logging into the Umbraco backoffice, the same OAuth flow the
backoffice's own login uses. Its permissions are the permissions of the person who authorises it.

1. **Log into the backoffice** at `https://localhost:44399/umbraco` (`admin@example.test` /
   `Wayfinder123!`). Leave the tab open. Acts 3 and 4 reuse this session.

2. **Register the server**, from a directory outside this repo (so you are working only through
   the MCP tools):

   ```bash
   cd "$(mktemp -d)"
   export NODE_TLS_REJECT_UNAUTHORIZED=0   # the Claude CLI's Node HTTP client doesn't read the
                                           # OS trust store, so it rejects the self-signed dev cert
   claude mcp add --transport http wayfinder-umbraco \
     https://localhost:44399/wayfinder/service-blueprint-authoring/mcp \
     --client-id umbraco-back-office-wayfinder-mcp \
     --callback-port 33418
   ```

   - `--client-id umbraco-back-office-wayfinder-mcp` is the public (PKCE, no secret) OAuth client
     the host registers at startup (`WayfinderMcpOAuthClientInstaller`).
   - `--callback-port 33418` matches the loopback redirect URI registered for the Development
     environment. Claude Code otherwise picks a random port, which would not be a registered
     redirect URI.

3. **Authorise:**

   ```bash
   claude mcp login wayfinder-umbraco
   ```

   Your browser opens on the backoffice authorize page. Because you are already signed in, it
   goes straight through the consent step and redirects back to `localhost:33418`, where the CLI
   is listening. On a machine with no browser (SSH), add `--no-browser`: the CLI prints the URL,
   you open it elsewhere, and paste the `localhost:33418/callback?...` URL back when prompted.

4. **Confirm:**

   ```bash
   claude mcp list      # wayfinder-umbraco  ✔ Connected
   ```

The token is your own backoffice identity, short-lived, and refreshed automatically. `BlueprintsAdmin`
is checked against your real group membership.

> **Headless or CI?** The client-credentials flow (a hand-minted, non-refreshing token passed as
> `--header`) also works. See `Wayfinder.Umbraco.ReferenceApp/README.md`, "Connecting an MCP
> client (headless / CI)".

---

## Act 2: hand over the brief

1. **Launch Claude Code with the MCP toolkit**, still in the scratch directory:

   ```bash
   claude --model sonnet \
     --tools "mcp__wayfinder-umbraco__*,ListMcpResourcesTool,ReadMcpResourceDirTool,ReadMcpResourceTool" \
     --permission-mode bypassPermissions
   ```

   `--tools` scopes the session to this MCP server's tools plus the built-in MCP-resource readers.
   `bypassPermissions` is reasonable here because the toolset is already that narrow, against a
   local dev stack. Answer the one-time workspace-trust and bypass-permissions prompts if they
   appear.

2. **Paste this brief.** It is pure domain language. The MCP's own resources and skills teach the
   agent the mechanics (stages, routing, conditions, component types); the brief does not.

   > Hi. I work on licensing for the National Juggling Authority. I need help designing a new service.
   >
   > The problem: right now, if someone already holds a current professional juggling licence from another recognised juggling authority and wants to work here, they have to apply for a brand new licence from scratch, exactly the same as someone who's never juggled professionally before. That's not fair on them, it duplicates assessment work that's already been done properly elsewhere, and it puts off exactly the experienced jugglers we want performing here.
   >
   > I want a "transfer your licence" service instead. What I know about how it needs to work:
   > - Only for jugglers who already hold a current licence from a juggling authority we formally recognise. Right now that's the European Juggling Federation, Async Circle International, and the Ring Masters Guild. Anyone else isn't eligible for transfer; they need to apply as a new licence holder instead, which is a separate existing service.
   > - We need to see their current licence certificate and some proof of who they are.
   > - Before we grant anything, they need to formally declare they'll uphold our professional standards, the same declaration a new applicant makes.
   > - A caseworker always has to check the evidence and make the actual decision. This can't be auto-approved, someone has to look at the documents.
   > - Same accessibility bar as everything else we ship: WCAG double-A, in line with the GDS service standard.
   >
   > Can you help me design this properly? Ask me anything you need.

3. **Answer its clarifying questions in the same plain language.** A good conversation asks about
   expiry dates, what "proof of identity" means to you, what happens to a rejected transfer.
   Answer as the service owner would. You do not need to know how any of it is implemented.

4. **The agent designs, validates, fixes what it flags, and saves.** It is done when it tells you
   it has saved the blueprint. It chooses its own internal key and display name; the brief never
   dictates either.

You can watch progress in the backoffice: **Settings → Blueprints** shows the new entry appear
alongside `reference-demo` the moment the first save lands.

---

## Act 3: publish it to the site

Point the existing `/apply` page at what the agent built. No restart, no redeploy.

1. Backoffice → **Content** → open the **Apply** page.
2. Click the **service request stage** block on that page. Its **Blueprint key** currently reads
   `reference-demo`.
3. Change **Blueprint key** to the agent's new key (from Settings → Blueprints, or the agent's own
   summary), then click **Update** on the block.
4. **Save and publish** the page. Wait for the "published" confirmation.

`/apply` now serves the transfer-licence service.

---

## Act 4: review it in the visual editor

1. Backoffice → **Settings** → **Blueprints** → open the new blueprint by its display name.
2. The **visual editor** opens on the graph. Click **Fit to screen** to see every stage, decision
   point, and route the agent wrote.
3. **Find the decision point.** Click the stage with two or more outgoing routes. Each route has
   its own **Available when** condition. This is where "only jugglers from a recognised authority
   can transfer" became routing logic, evaluated before the applicant reaches the rest of the
   form. The other route is the "apply as a new licence holder instead" path.
4. Round the graph you will also see the **document-upload** stage (the licence certificate and
   proof of identity you asked to see) and the **caseworker review** stage (the decision you said
   always needs a person).
5. Open the **Validation** tab: a clean, valid definition.

Everything here traces back to a line in the brief. Nothing in the brief named a stage, a
condition, or a component type.

---

## Act 5: run the service

**As the applicant:**

1. Go to `https://localhost:44399/demo/login`, choose **Alex Applicant** (no password).
2. Click **Apply** and walk the journey the agent designed: answer the eligibility question (pick
   a recognised authority so you stay on the transfer path), upload your PDF wherever a file is
   asked for, tick the declaration, fill any remaining fields, and submit each stage through to
   the confirmation screen.

**As the caseworker:**

3. **Sign out**, go back to `/demo/login`, choose **Casey Caseworker**.
4. Click **Caseworker queue**. The transfer request is there: the blueprint's own routing put it
   in the right place. Pick it up to see the applicant's answers and the documents they uploaded,
   and make the decision.

One brief, one conversation, a working service: described in plain language, published in Umbraco,
and run by real people.

---

## Recording this

The video is produced by `npm run demo:record` from `tests/demo/`. See `tests/demo/README.md` for
the operator setup. That script performs exactly the steps above; the "designer" answers in Act 2
are generated by a small model so the take is repeatable.
