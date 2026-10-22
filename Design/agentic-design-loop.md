# The agentic design loop

**Status:** proposal. Nothing here is built yet. Most of the work lands in
[`jonnymuir/Wayfinder`](https://github.com/jonnymuir/Wayfinder) (editor, MCP tools, contracts);
this repo supplies storage, preview and the backoffice surfaces. Each part says which repo it
touches.

## The problem

Wayfinder's biggest strength is that a service designer can describe a problem in plain language
and an AI design partner, connected over MCP, turns it into a working service blueprint. The
conversation itself works well. The round trip around it does not:

1. **The canvas doesn't follow the agent.** The editor polls every 15 seconds and notices a newer
   version, but treats it as a conflict: the canvas goes read-only behind a "changed elsewhere"
   banner until the designer reloads. Watching an agent build a service means clicking Reload
   after every save, with no sign of what changed.
2. **Saves are live.** `save_service_blueprint` and the editor both write the one stored row,
   and the engine picks it up at once. An agent iterating on a running service changes it for real
   applicants with every save, and there is no history to go back to.
3. **Seeing the journey takes a publish.** To walk the service, the designer edits the Blueprint
   key on a page's block and publishes the page. To see the caseworker side, they sign out and
   back in as someone else.
4. **Feedback is prose.** The designer looks at the canvas or walks the journey, then describes
   what they saw to the agent in words: "the third question on the eligibility bit". The agent
   can't see what the designer is pointing at.
5. **Connecting takes a page of instructions.** A dev certificate, `NODE_EXTRA_CA_CERTS`,
   `claude mcp add` with a client ID and callback port, `mcp login`, then a launch with tool
   scoping flags.

## The loop we want

> Describe it. Watch it appear. Walk it. Point at what's wrong. "Work through my notes." Repeat.
> Publish when it's right.

The designer chats with the agent in one window and keeps the canvas open in another. Each save
appears on the canvas with the changes highlighted. A preview link walks the draft journey as any
person in the service. Anything that's wrong gets a note pinned to the exact stage, from the
canvas or from the preview. The agent reads the notes, fixes them, and marks each one resolved
with what it did. Nothing reaches real applicants until the designer publishes.

Every part works with any MCP client. Wayfinder is used with Claude Code, Claude Desktop, VS Code
and GitHub Copilot, and others, so nothing here depends on one of them.

## Part 1: the canvas follows the agent

**Repo:** Wayfinder (`Wayfinder.Editor.Client`). Wayfinder.Umbraco picks it up by upgrading.

`StalenessController` already finds newer versions. Change what happens next:

- **No unsaved edits:** reload in place. Keep the zoom, pan and selection (when the selected
  stage still exists), and highlight what changed for a few seconds: added stages and routes in
  one colour, changed ones in another. A short summary, such as "Version 7: 2 stages added, 1
  route changed, 1 stage removed", appears in a polite live region, so a screen reader announces
  it, and stays in a "Recent changes" list until the designer dismisses it.
- **Unsaved edits:** the existing conflict banner and read-only overlay, unchanged. Keeping a
  designer's own work safe still wins.
- **Polling:** every 3 seconds for 10 minutes after any external save is seen, otherwise every 15.
  It is a cheap scalar check. Server-Sent Events stays the upgrade path the controller's own
  comment names, if polling ever costs too much.

The diff is computed in the client between the two snapshots, by stage key, route (from, to and
condition) and field key, so it needs no server change. A pure, unit-tested `diffBlueprints`
function also serves Part 2's history view and Part 3's "resolved in version 7" links.

## Part 2: drafts, preview and history

**Repos:** Wayfinder (contracts, MCP tools, editor), Wayfinder.Umbraco (storage, preview route,
backoffice). This is a breaking change for Wayfinder.Umbraco, because saving stops being live.

### Drafts

Each blueprint has a **published** version, which the engine runs, and at most one **draft**.

- The editor and `save_service_blueprint` save to the draft. Optimistic concurrency works as it
  does today, against the draft's version.
- `read_service_blueprint` returns the draft when there is one, and says so. A `published: true`
  argument reads the live one.
- **Publish** in the backoffice makes the draft live and clears it. Publishing is the designer's
  act: no MCP tool publishes by default (see open questions).
- **Discard draft** throws it away.
- A new blueprint is only a draft until its first publish, so it can't be put on a page before
  then.

### History

Every publish is kept as a version, and so is every draft save, capped at the last 50 draft
saves for each blueprint. The canvas gets a History panel: pick a version to see it with Part 1's
diff against the current draft, then **Restore as draft**. "Go back to before the agent's last
change" is one click, and restoring never touches the live service.

Storage: a `wayfinderServiceBlueprintVersion` table (definition key, version, JSON, kind of
publish or draft, author, saved at), beside the existing row, which becomes the published
pointer. uSync carries only the published version.

### Preview

`/wayfinder/preview/{key}` runs the draft as a journey, for backoffice users with `BlueprintsAdmin`.

- The preview runs in a sandbox: its requests are marked as previews, never appear in real
  worklists, and are deleted after 24 hours.
- Side effects are suppressed. Emails go to a preview outbox shown in the preview toolbar, and
  support system calls use the stub the blueprint declares, or are shown as "would call X with
  Y".
- A **preview toolbar** lets the designer act as any queue the blueprint names: the applicant,
  then the caseworker who picks the request up, then back. There's no sign-out, because preview
  people exist only in the sandbox.
- `save_service_blueprint` returns the preview URL with the save result, so the agent can hand it
  over ("Open the preview to try the ineligible path"). `simulate_service_blueprint` can return
  a URL that opens the preview at the end of the path it simulated.

## Part 3: design notes

**Repos:** Wayfinder (model, `IDesignNoteStore` contract, MCP tools, canvas UI), Wayfinder.Umbraco
(table, preview Feedback button).

This is what turns feedback from prose into pointing.

### A note

| Field | What it is |
|---|---|
| Id | |
| Blueprint key | |
| Anchor | What it's pinned to: a stage key, a route, a field key, or the whole blueprint. |
| Text | What the designer, or the agent, wants to say. |
| Author | The person, or "design partner" when the agent wrote it. |
| Status | Open or resolved. |
| Resolution | What was done, in a sentence, and the version it was done in. |
| Created, resolved | Timestamps. |

Notes live beside the blueprint, not inside its JSON. They are about the design, not part of it,
and they must not change the blueprint's version or travel with it into a published service.

### Making notes

- **On the canvas:** select a stage, route or field and choose **Add note**. A marker with a count
  appears on the stage.
- **In the preview:** a **Feedback** button on every preview page opens a note already anchored to
  the stage on screen, with the field in focus if there is one. "This question is confusing" is
  written while looking at the question.
- **From the agent:** the agent can pin a question to a stage instead of asking in the chat, for
  example "Should an expired licence be refused here, or sent to a caseworker?". The designer
  answers on the canvas, where the context is.

### The agent working through notes

New MCP tools:

| Tool | What it does |
|---|---|
| `list_design_notes` | Open notes for a blueprint (or all, by status), each with its anchor resolved to the stage's name and the current JSON path, so the agent knows exactly where it points. |
| `add_design_note` | The agent pins a question or a flag to an anchor. |
| `resolve_design_note` | Marks a note resolved, with a one-sentence summary and the draft version that resolved it. |
| `reply_to_design_note` | A threaded reply, for when the agent needs to ask before it can resolve. |

"Work through my notes" becomes: list the open notes, change the draft, save, resolve each one
with what it did. The canvas, following along through Part 1, shows each marker turn resolved,
and each resolution links to the diff for its version. The authoring guide resource gains a short
section that teaches this cycle, so any agent picks it up without being told.

If a note's stage is deleted, the note stays and moves to the blueprint level, marked "stage
removed", so nothing the designer said disappears silently.

## Part 4: connecting in one step

**Repo:** Wayfinder.Umbraco.

A **Connect a design partner** panel on the Blueprints screen shows, for the current site:

- the exact setup for Claude Code, Claude Desktop, VS Code and a generic MCP client, with the
  server URL, client ID and callback port filled in, each with a copy button;
- a downloadable `.mcp.json`;
- a starter prompt for a new service, and one for "work through my notes on {blueprint}".

The dev-certificate step only applies to a local site and stays in the walkthrough.

## Part 5 (spike): the design partner inside the backoffice

**Repo:** Wayfinder.Umbraco, as an optional `Wayfinder.Umbraco.AI` add-on.

Umbraco.AI has an in-backoffice Copilot chat with agents, and packages can give it native tools:
`Umbraco.AI.Automate` does this for Automate. A `Wayfinder.Umbraco.AI` add-on could give it the
authoring tools above and a "Service designer" agent whose instructions are the authoring guide.
The chat would know which blueprint is open, so "add a step after this one" works, and the canvas
sits beside it. There would be no terminal and no certificates, the person's own backoffice
permissions would apply, and any model provider Umbraco.AI supports would work.

The spike answers:

1. Does Umbraco.AI's tool contract map cleanly onto the existing tools, so one implementation
   serves both MCP and Umbraco.AI?
2. Which Umbraco versions does Umbraco.AI's Copilot support, and does that match ours (17)?
3. Can the chat know the open blueprint and selected stage, through Umbraco.AI's context?
4. Does a general chat loop design as well as an MCP client like Claude Code? Run the juggling
   licence transfer brief through both and compare the blueprints.

Build only if the answer to 4 is close enough. The MCP route stays either way, for designers who
want a stronger agent.

## Considered and not proposed: running the Claude CLI inside the UI

This would mean a shell process on the web server for each designer:

- **Security:** a shell on the server is a large risk, even with tools scoped down.
- **Accounts and billing:** each designer's Claude account and usage would have to live on the
  server.
- **Lock-in:** it ties the product to one vendor.

A server-side agent built on an SDK, with no shell and only the Wayfinder tools, avoids the first
problem but not the last. Part 5 gets the same "it's in the UI" result without either.

## Order of work

1. **Part 1**, the canvas follows the agent. Small, Wayfinder only, and every later part leans on
   its diff.
2. **Part 3**, design notes. The biggest change to how the loop feels, and it works with saves as
   they are today.
3. **Part 2**, drafts, preview and history. The largest piece, and breaking for Wayfinder.Umbraco,
   so it gets its own major version.
4. **Part 4**, the connect panel. Small, any time.
5. **Part 5**, the spike, after Part 3, so the in-backoffice agent has notes to work through.

## Open questions

1. **Should an agent ever publish?** Proposed: no tool by default. A host could opt in to a
   `publish_service_blueprint` tool for teams that want the agent to ship.
2. **What happens to requests in flight when a blueprint is published?** A service request doesn't
   record which blueprint version it started on, so today every save changes the rules for
   requests already under way. Drafts turn that into one deliberate moment, the publish, but
   don't answer it. Whether a request finishes on the version it started on is a question for the
   engine, and worth its own design.
3. **Do notes travel with uSync?** Proposed: no. They are working material for one environment.
4. **Who can see notes?** Proposed: anyone with `BlueprintsAdmin`, which is who can edit the
   blueprint.
5. **Preview data retention:** 24 hours, or until the draft is published or discarded, whichever
   comes first?
