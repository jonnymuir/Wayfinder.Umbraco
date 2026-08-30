# Authoring a public service over MCP — walkthrough

This is the exact sequence the demo video (`tests/demo/mcp-authoring-demo.spec.ts`) records,
written so you can do it yourself against a local `Wayfinder.Umbraco.ReferenceApp`. Every command
and click below is what the recording performs; the video mirrors this document.

**What it produces:** an AI agent, given nothing but backoffice-authenticated access to this
host's MCP authoring toolkit, designs and saves a complete branching "transfer your juggling
licence" service from a single plain-language brief. You then wire it into the live site, review
it in the visual editor, and run it end to end as a citizen and a caseworker.

---

## Before you start

Boot the reference app from a **fresh database** (so the run starts from the seeded
`reference-demo` placeholder, and Act 1 isn't tripped by a stale MCP client entry):

```bash
rm -rf Wayfinder.Umbraco.ReferenceApp/umbraco/Data/*
Umbraco__CMS__Global__TimeOut=02:00:00 \
  dotnet run --project Wayfinder.Umbraco.ReferenceApp --launch-profile Wayfinder.Umbraco.ReferenceApp
```

Wait for it to finish its unattended install and serve `https://localhost:44399`. The
`Umbraco__CMS__Global__TimeOut` is not optional for a real sit-down: Act 2's agent call routinely
runs past Umbraco's ~20-minute default backoffice session timeout, and Act 3 (which reuses your
Act 1 login) then fails with the session already expired.

You need:

| | |
|---|---|
| **Backoffice login** | `admin@example.test` / `Wayfinder123!` |
| **Claude Code** | signed in, `claude` on your `PATH` (`claude --version` ≥ 2.1.x — needs `--client-id` / `--callback-port` on `claude mcp add`) |
| **A terminal** | for Act 1–2 |
| **A one-page PDF** | any real PDF, for the Act 5 uploads. The recording uses a throwaway `juggling-licence-evidence.pdf`. |

---

## Act 1 — give the agent real access

The agent connects by logging into the Umbraco backoffice — the same OAuth flow the backoffice's
own login uses. No API user to create, no token to mint by hand.

1. **Log into the backoffice** at `https://localhost:44399/umbraco` (`admin@example.test` /
   `Wayfinder123!`). Leave the tab open — Acts 3 and 4 reuse this session.

2. **In a terminal**, from any directory *outside* this repo (a scratch directory keeps the
   framing honest — the agent gets MCP tools, not filesystem access to the code):

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
   - `--callback-port 33418` matches the loopback redirect URI registered **for the Development
     environment**. Claude Code otherwise picks a random port, which wouldn't be a registered
     redirect URI.

3. **Authorise in the browser** that opens (or visit the URL the CLI prints). Because you're
   already signed into the backoffice, this goes straight through to a consent step and back;
   otherwise you'll log in first. The CLI reports the connection once the callback lands.

4. **Confirm:**

   ```bash
   claude mcp list      # wayfinder-umbraco  ✔ Connected
   ```

The token is your own backoffice identity, short-lived, and **refreshed automatically** — no
re-auth mid-session. `BlueprintsAdmin` is checked against your real group membership.

> **Headless / CI instead?** The client-credentials flow (a hand-minted, non-refreshing token
> passed as `--header`) still works — see `Wayfinder.Umbraco.ReferenceApp/README.md`,
> "Connecting an MCP client (headless / CI)".

---

## Act 2 — hand over the brief

1. **Launch Claude Code restricted to the MCP toolkit**, still in the scratch directory:

   ```bash
   claude --model sonnet \
     --tools "mcp__wayfinder-umbraco__*,ListMcpResourcesTool,ReadMcpResourceDirTool,ReadMcpResourceTool" \
     --permission-mode bypassPermissions
   ```

   `--tools` narrows the *entire* session to this MCP server's tools plus the built-in
   MCP-resource readers — no `Bash`, no `Task`, no file access. `bypassPermissions` is safe
   *because* the toolset is already that narrow, against a local dev stack. Answer the one-time
   workspace-trust and bypass-permissions prompts if they appear.

2. **Paste this brief verbatim** — it is pure domain language, no Wayfinder terminology. The
   MCP's own resources and skills are what teach the agent the mechanics (routes, gateways,
   conditions, component types); the brief must not.

   > Hi. I work on licensing for the National Juggling Authority. I need help designing a new service.
   >
   > The problem: right now, if someone already holds a current professional juggling licence from another recognised juggling authority and wants to work here, they have to apply for a brand new licence from scratch — exactly the same as someone who's never juggled professionally before. That's not fair on them, it duplicates assessment work that's already been done properly elsewhere, and it puts off exactly the experienced jugglers we want performing here.
   >
   > I want a "transfer your licence" service instead. What I know about how it needs to work:
   > - Only for jugglers who already hold a current licence from a juggling authority we formally recognise — right now that's the European Juggling Federation, Async Circle International, and the Ring Masters Guild. Anyone else isn't eligible for transfer; they need to apply as a new licence holder instead, which is a separate existing service.
   > - We need to see their current licence certificate and some proof of who they are.
   > - Before we grant anything, they need to formally declare they'll uphold our professional standards — same declaration a new applicant makes.
   > - A caseworker always has to check the evidence and make the actual decision — this can't be auto-approved, someone has to look at the documents.
   > - Same accessibility bar as everything else we ship — WCAG double-A, in line with the GDS service standard.
   >
   > Can you help me design this properly? Ask me anything you need.

3. **Answer its clarifying questions in the same plain domain language.** A good conversation
   asks real questions — about expiry dates, what "proof of identity" means to you, what happens
   to a rejected transfer. Answer as the service owner would; you don't need to know how any of it
   is implemented. (In the recording a small Claude model plays this role so the take is
   repeatable — for a real sit-down, that's just you.)

4. **The agent designs, validates, fixes what it flags, and saves.** It's done when it tells you
   it has saved the blueprint. It chose its own internal key and display name — you'll see them
   in the next step; the brief deliberately never dictated either.

You can watch progress without interrupting: in the backoffice, **Settings → Blueprints** shows
the new entry appear alongside `reference-demo` the moment the agent's first save lands.

---

## Act 3 — wire it into the site

No restart, no redeploy — point the existing `/apply` page at what the agent built.

1. Backoffice → **Content** → open the **Apply** page.
2. Click the **service request stage** block on that page. Its **Blueprint key** currently reads
   `reference-demo`.
3. Change **Blueprint key** to the agent's new key (from Settings → Blueprints, or the agent's
   own summary), click **Update** on the block.
4. **Save and publish** the page. Wait for the "published" confirmation.

`/apply` now serves the transfer-licence service.

---

## Act 4 — review what it built

1. Backoffice → **Settings** → **Blueprints** → open the new blueprint by its display name.
2. The **visual editor** opens on the graph. Click **Fit to screen** — you're looking at every
   stage, gateway, and route the agent wrote.
3. **Find the branch point.** Click the stage with two or more outgoing routes. Each route has
   its own **Available when** condition — this is where the "only jugglers from a recognised
   authority can transfer" rule became real routing logic, evaluated before the applicant reaches
   the rest of the form. The other route is the "apply as a new licence holder instead" path.
4. Round the graph you'll also see the **document-upload** stage (the licence certificate and
   proof of identity you asked to see) and the **caseworker review** stage (the decision you said
   always needs a person).
5. Open the **Validation** tab — a clean, valid definition, exactly as the agent left it.

Everything here maps back to a line in your brief. Nothing in the brief named a stage, a
condition, or a component type.

---

## Act 5 — run it end to end

**As the applicant:**

1. Go to `https://localhost:44399/demo/login`, choose **Alex Applicant** (no password).
2. Click **Apply** and walk the journey the agent designed — answer the eligibility question
   (pick a recognised authority so you stay on the transfer path), upload your PDF wherever a
   file is asked for, tick the declaration, fill any remaining fields, and submit each stage
   through to the confirmation screen.

**As the caseworker:**

3. **Sign out**, go back to `/demo/login`, choose **Casey Caseworker**.
4. Click **Caseworker queue**. The transfer request is sitting there — the agent's own routing
   put it in the right place. Pick it up to see the applicant's answers and the documents they
   uploaded, and make the decision.

That's the whole loop: a real backoffice identity, a real MCP connection, an AI-authored
branching service — wired into the live site, reviewed in the visual editor, and run end to end
by a real applicant and a real caseworker.

---

## Recording this

The video is produced by `npm run demo:record` from `tests/demo/` — see `tests/demo/README.md`
for the operator setup. That script performs exactly the steps above; the "designer" answers in
Act 2 are generated by a small model so the take is repeatable.
