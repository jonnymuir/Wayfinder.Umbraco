# Building this site: rules for AI agents

This Umbraco site uses **Wayfinder for Umbraco** (service blueprints) and may use **Umbraco
Prism** (tenants and branding). Services here are configuration, not code. Follow these rules
whichever tool you are.

## The rule

Every form, step, decision, calculation, staff screen and call to a business system is a
**Wayfinder service blueprint**. Umbraco content is only for pages that inform (home, about,
guidance, news) and for the page a service sits on. The look comes from design tokens over the
GOV.UK Design System markup.

## Where things go

| Building | Goes in | Never |
|---|---|---|
| A form or journey with steps | A blueprint (Settings → Advanced → Blueprints), on a page with the **Wayfinder Service Request Stage** block | A document type per step, a `SurfaceController`, a form handler |
| Branching, eligibility, validation | Gateways, route `showWhen`, component validation in the blueprint | C#, Razor or JavaScript |
| Sums, estimates, lookups | The blueprint's `calculations` block | A service class or a script |
| Staff queues, triage, approvals | A backstage queue in the blueprint, on a page with the **Wayfinder Service Request Worklist** block | A backoffice dashboard or custom admin page |
| Data from a business system | A support system in `Wayfinder:SupportSystems` (`appsettings.json`), with `mock: true` until the real API exists | An `HttpClient` in a controller or view |
| Facts about the signed-in member | An `IServiceInputsResolver` | Claims read in a view |
| Colours, type, spacing | Design tokens (`--govuk-*`, or the Prism tenant's `--prism-*` tokens) | Copied GOV.UK markup, a second CSS framework |
| Editorial pages | Umbraco content | A blueprint |

## Stop if you are about to

- create a document type or template for a question, check-your-answers or confirmation page;
- write a controller or endpoint that receives a submission, or store answers yourself;
- put business rules or arithmetic in C#, Razor or JavaScript;
- build a staff screen that lists cases;
- call a business system's API directly.

Each has a home in the table. If something truly does not fit, report it as a gap with the
closest Wayfinder way of doing it, and let the person who owns the design decide.

## Rebuilding an existing design

1. Inventory every page and interaction: content, journey stage, staff task, data lookup,
   notification, or styling.
2. Keep editorial pages as content; strip out anything that collects answers.
3. One blueprint per service, with its queues named.
4. Every value the person did not type comes from a service input or a (mocked) support system.
   Write down what each mock assumes about the real system.
5. Map the design's look onto tokens.
6. Report the gaps and wait for decisions. Then build blueprint first, content and styling last.

## How to author blueprints

- Connect to the site's MCP endpoint (Settings → Advanced → Blueprints → **Connect a design
  partner**). Its resources teach the blueprint format; its prompts (`design-a-service`,
  `convert-an-existing-design`, `work-through-design-notes`) start each kind of work.
- Read `service-blueprint-docs://building-with-wayfinder` and
  `service-blueprint-docs://authoring-guide` first. Call `list_component_types` and
  `list_support_systems` to see what this site offers.
- Validate and simulate every route before saving. Saving is live on the site you are connected
  to, so work on a development site.

## Read more

- Building a site with Wayfinder for Umbraco:
  https://github.com/jonnymuir/Wayfinder.Umbraco/blob/main/docs/building-a-site.md
- Building a service with Wayfinder:
  https://github.com/jonnymuir/Wayfinder/blob/main/docs/guides/building-with-wayfinder.md
- Mocking a support system:
  https://github.com/jonnymuir/Wayfinder/blob/main/docs/guides/support-systems.md#mocking-a-support-system
- Umbraco Prism branding and the GOV.UK token bridge:
  https://github.com/jonnymuir/Umbraco.Prism/blob/main/docs/guides/govuk-token-bridge.md
