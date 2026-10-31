# Building a site with Wayfinder for Umbraco

This guide is for whoever is building an Umbraco site with Wayfinder for Umbraco installed: a
developer, or an AI agent working for one. Read it before creating any document type, template
or controller. It applies Wayfinder's
[Building a service with Wayfinder](https://github.com/jonnymuir/Wayfinder/blob/main/docs/guides/building-with-wayfinder.md)
to Umbraco, and covers [Umbraco Prism](https://github.com/jonnymuir/Umbraco.Prism) where a site
uses it for tenants and branding.

**The rule:** every form, step, decision, calculation, staff screen and call to a business system
is a Wayfinder service blueprint. Umbraco content is for the pages that only inform, and for the
page a service sits on. The look comes from design tokens over the GOV.UK Design System markup.

Umbraco makes it easy to build a form as document types, templates and a surface controller, and
an agent asked to "build the site in Umbraco" will do exactly that unless told otherwise. That
version of a service cannot be seen in the blueprint editor, cannot be changed by a designer or a
design partner, has no caseworker worklist, and is code to maintain. The Wayfinder version is a
blueprint, a block on a page, and a few lines of configuration.

## Where each part of a site lives

| What you are building | In Umbraco with Wayfinder | Not |
|---|---|---|
| Home, about, guidance, news, campaign pages | Ordinary Umbraco content: document types, templates, the content tree | A blueprint |
| Navigation, header, footer, layout | Your Umbraco templates and layout | A blueprint |
| An application, enquiry, registration, claim, booking, or any form with steps | A blueprint under **Settings → Advanced → Blueprints**, placed on a page with the **Wayfinder Service Request Stage** block | A document type per step, a surface controller, a form handler |
| A staff queue, triage or approval screen | A backstage queue in the blueprint, worked from a page with the **Wayfinder Service Request Worklist** block | A backoffice dashboard or a custom admin page |
| Who sees which queue | `ResolveAccessProfile`, from your members, roles or claims | Checks in views |
| Facts about the signed-in member that a service starts from | An `IServiceInputsResolver` | Values read from claims in a view |
| Member, scheme or case data from a business system | A support system in `Wayfinder:SupportSystems`, mocked until the real system is ready | An `HttpClient` in a controller or view component |
| Emails as a request moves on | `AddWayfinderEmails` | A notification handler that builds its own email |
| Colours, type, spacing | Design tokens over GOV.UK Frontend; with Prism, the tenant's design system | Replacement markup, a second CSS framework |
| One site serving several brands | Umbraco Prism tenants, each with its own branding and sign-in | Copies of the site |

## Signs the build has gone the wrong way

Stop and move the work into a blueprint when the site has, or is about to have:

- a document type or template for a question page, a check-your-answers page or a confirmation;
- a `SurfaceController`, an API controller or a minimal API endpoint that receives a submission;
- a custom table, a content node or session state holding someone's answers;
- branching, eligibility or arithmetic in C#, Razor or JavaScript;
- a backoffice section, dashboard or page listing cases for staff;
- GOV.UK markup copied into a Razor view to change how it looks;
- an `HttpClient` calling a business system from a controller, view or view component.

When something genuinely has no home in a blueprint, say so as a gap, with the closest Wayfinder
way of doing it, and let whoever owns the design decide.

## Rebuilding an existing design or site

Given a finished design (from a design tool, a prototype, or a site already built as Umbraco
content):

1. **Inventory it.** Every page and interaction, each marked as content, a stage in a journey, a
   staff task, a data lookup, a notification or styling.
2. **Keep the content as content.** Editorial pages stay Umbraco pages. Strip out anything that
   collects answers or makes decisions.
3. **One blueprint per service.** Group the journey pages into services and name each one's
   queues: the person using it, each team, and a system lane (`actor: system`) for calls to other
   systems.
4. **List the data.** Every value shown that the person did not type comes from a service input
   or a support system. Mock the business system (below) and write down what each mock assumes.
5. **Map the look onto tokens.** Colours, type and spacing become design tokens (see
   [Styling](#styling)). Note what the GOV.UK components cannot show rather than overriding their
   markup.
6. **Report gaps, then build.** Agree the gaps, then build blueprint first: author, validate,
   simulate and save each blueprint, put each on a page with the stage block, and finish with
   content and styling.

With the [MCP authoring endpoint](../README.md#design-in-conversation-over-mcp) connected, the
`convert-an-existing-design` prompt walks an agent through exactly these steps.

## Business system data, mocked first

A Wayfinder service gets data from a business system through a **support system**: a stage in a
system lane calls one of the system's capabilities, the answer picks the route out, and the
capability's outputs land in the request for later stages to show or calculate with. A member
lookup, a scheme lookup, opening a case and recording a decision are all capabilities.

Before the real system's API exists, declare it as a mock in `appsettings.json`.
Wayfinder.Umbraco registers every `Wayfinder:SupportSystems` entry on boot, so this is all a mock
needs:

```jsonc
"Wayfinder": {
  "SupportSystems": [{
    "key": "business-system",
    "displayName": "Business system (mock)",
    "mock": true,
    "capabilities": [{
      "key": "get-member",
      "displayName": "Look up a member",
      "inputs":  [ { "key": "memberNumber", "format": "field-ref", "required": true } ],
      "outputs": [ { "key": "memberName" }, { "key": "schemeName" } ],
      "outcomes": [ { "key": "found" }, { "key": "not-found" } ],
      "mock": {
        "outcome": "found",
        "outputs": { "memberName": "Alex Morgan", "schemeName": "Example Pension Scheme" },
        "cases": [ { "when": { "memberNumber": "00000000" }, "outcome": "not-found" } ]
      }
    }]
  }]
}
```

The blueprint calls `get-member` exactly as it would call the real system. When the API is
ready, replace `mock` with an `endpoint` (an Umbraco Automate automation, or any service that
accepts the webhook contract, see
[Connect other systems with Umbraco Automate](../README.md#connect-other-systems-with-umbraco-automate)),
and the blueprint does not change. Keep a short note beside the mock of what each input and output
means and what the mock assumes; that note is the brief for the real integration. The full
reference is Wayfinder's
[Mocking a support system](https://github.com/jonnymuir/Wayfinder/blob/main/docs/guides/support-systems.md#mocking-a-support-system).

Facts the service needs from its very first step, such as the signed-in member's own name and
number, come from an `IServiceInputsResolver` instead (see
[What a host still owns](../README.md#what-a-host-still-owns)). A mocked resolver returns fixed
values until the real source is ready.

## Styling

Wayfinder renders the real GOV.UK Design System, which carries its accessibility work with it.
Change how it looks with tokens, not markup:

- **Any site:** load your own stylesheet after `govuk-frontend.min.css` and set GOV.UK Frontend's
  `--govuk-*` colour custom properties (brand colour, link colours, focus colour and the rest) to
  your palette.
- **With Umbraco Prism:** each tenant edits its own design system under **Settings → Advanced →
  Tenants**, and the
  [GOV.UK token bridge](https://github.com/jonnymuir/Umbraco.Prism/blob/main/docs/guides/govuk-token-bridge.md)
  points the `--govuk-*` properties at the tenant's `--prism-*` tokens, so a tenant's colours
  reach every Wayfinder component without overriding any markup. See Prism's
  [branding design system](https://github.com/jonnymuir/Umbraco.Prism/blob/main/docs/branding-design-system.md)
  for how a design's tokens become editable fields.
- **Only when tokens cannot do it**, replace one component's markup with a Razor partial (see
  [Customising how components render](customising-rendering.md)), and take on its accessibility
  yourself.

## What code a Wayfinder site still has

A Wayfinder for Umbraco site should end up with no more code than:

- `ResolveAccessProfile` (and `ResolveTenantId` with more than one tenant), mapping your members
  and staff onto queues;
- an `IServiceInputsResolver`, if services start from facts about the signed-in member;
- file storage, if a service takes uploads;
- MCP wiring in `Program.cs`, if designers author in conversation;
- a stylesheet of tokens.

Everything else is a blueprint, an `appsettings.json` entry, or Umbraco content.

## Giving your AI agent these rules

An agent only follows rules it can read. In the site's repository, run:

```bash
dotnet new install Wayfinder.Umbraco.Templates
dotnet new wayfinder-agents
```

It adds `AGENTS.md`, the short form of this guide that most AI coding tools read, and a
`CLAUDE.md` that points Claude Code at it. Connect the agent to the site's MCP endpoint too
(**Settings → Advanced → Blueprints → Connect a design partner**), so it can read the authoring
guides and use the prompts.

## Related documentation

- [How Wayfinder works in Umbraco](how-it-works.md): blueprints, blocks and pages.
- [MCP authoring walkthrough](mcp-authoring-walkthrough.md): designing a service in conversation.
- [Umbraco Automate as a support system](automate-support-system-walkthrough.md): the real
  integration a mock is later swapped for.
