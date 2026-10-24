# The agentic design loop

**Status:** Parts 1 and 3 are built (Wayfinder 0.24.0, Wayfinder.Umbraco 5.1.0), and Part 4 is
built (Wayfinder.Umbraco 5.2.0). Part 2 is not planned, see below. Part 5 is a proposal. Most of the work lands in
[`jonnymuir/Wayfinder`](https://github.com/jonnymuir/Wayfinder) (editor, MCP tools, contracts);
this repo supplies storage and the backoffice surfaces. Each part says which repo it touches.

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
   applicants with every save. Part 2 answers this with environments, not drafts.
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
> Promote it when it's right.

The designer chats with the agent in one window and keeps the canvas open in another. Each save
appears on the canvas with the changes highlighted. The designer walks the journey on the same
site. Anything that's wrong gets a note pinned to the exact stage. The agent reads the notes,
fixes them, and marks each one resolved with what it did. All of this happens on a development
site, so nothing reaches real applicants until the blueprint is promoted to live.

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
function also serves Part 3's "resolved in version 7" links.

## Part 2: change control through environments

**Not planned.** An earlier version of this plan gave each blueprint a draft, a publish step, a
version history and a sandboxed preview. Wayfinder leaves change control to environments instead,
the same way Umbraco content and code move:

- A designer and their agent work on a local or development site, where saves going live at once
  is what they want.
- A blueprint moves to staging and then live through uSync, which already carries blueprints.
- Walking the journey on the development site is the preview.

Saves stay live, and the store keeps one version of each blueprint. Tooling and guidance for
promoting blueprints between environments, and for merging changes made in more than one, will be
designed once there is a live environment to learn from.

## Part 3: design notes

**Repos:** Wayfinder (model, `IDesignNoteStore` contract, MCP tools, canvas UI), Wayfinder.Umbraco
(table).

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
and they must not change the blueprint's version or travel with it to another environment.

### Making notes

- **On the canvas:** select a stage, route or field and choose **Add note**. A marker with a count
  appears on the stage.
- **From the agent:** the agent can pin a question to a stage instead of asking in the chat, for
  example "Should an expired licence be refused here, or sent to a caseworker?". The designer
  answers on the canvas, where the context is.

### The agent working through notes

New MCP tools:

| Tool | What it does |
|---|---|
| `list_design_notes` | Open notes for a blueprint (or all, by status), each with its anchor resolved to the stage's name and the current JSON path, so the agent knows exactly where it points. |
| `add_design_note` | The agent pins a question or a flag to an anchor. |
| `resolve_design_note` | Marks a note resolved, with a one-sentence summary and the version that resolved it. |
| `reply_to_design_note` | A threaded reply, for when the agent needs to ask before it can resolve. |

"Work through my notes" becomes: list the open notes, change the blueprint, save, resolve each one
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

1. **Part 1**, the canvas follows the agent. Built.
2. **Part 3**, design notes. Built.
3. **Part 4**, the connect panel. Built.
4. **Part 5**, the spike, so the in-backoffice agent has notes to work through.

## Open questions

1. **What happens to requests in flight when a blueprint changes?** A service request doesn't
   record which blueprint version it started on, so every save, and every promotion to live,
   changes the rules for requests already under way. Whether a request finishes on the version it
   started on is a question for the engine, and worth its own design.
2. **Promoting and merging between environments.** How a blueprint moves from development to
   staging to live, and what happens when two environments change the same one, is for a later
   design (see Part 2).

Settled while building Part 3: notes don't travel with uSync, because they are working material
for one environment, and anyone with `BlueprintsAdmin` can see and add them.
