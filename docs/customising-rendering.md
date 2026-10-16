# Customising how components render

Every component in a stage renders as GOV.UK Design System markup from
[`Wayfinder.Rendering.GovUk`](https://github.com/jonnymuir/Wayfinder/tree/main/Wayfinder.Rendering.GovUk),
the same catalog every Wayfinder host uses. You rarely need to change it: it carries the GOV.UK
Design System's accessibility work with it. When you do, Wayfinder.Umbraco lets your site replace
the markup for one component or field type at a time with a Razor partial of its own.

## Override one type

Place a partial in your own site at the convention path:

| What | Path in your site |
|---|---|
| A component (a panel, summary list, chart...) | `Views/Partials/Components/_Component-{Type}.cshtml` |
| An input field (text, date, file upload...) | `Views/Partials/Fields/_Component-{Type}.cshtml` |

`{Type}` is the component's `type` in PascalCase: `summary-list` becomes
`_Component-SummaryList.cshtml`, `file-upload` becomes `_Component-FileUpload.cshtml`. To catch
every type you haven't overridden individually, add `_Component-Default.cshtml` in the same
folder.

For each component, `ComponentPartialResolver` looks for your type-specific file first, then your
`_Component-Default.cshtml`, and falls back to the built-in GOV.UK markup if neither exists. It
remembers that decision for the life of the process, so a new override file takes effect when the
site restarts.

## What your partial receives

| Folder | Model | Useful members |
|---|---|---|
| `Components/` | `Wayfinder.Umbraco.Models.ComponentContext` | `Component` (the authored component), `Errors` and `Values` (by field key), `InstanceId`, `BlueprintKey`, `StateVersion`, `Nonce` |
| `Fields/` | `Wayfinder.Umbraco.Models.FieldContext` | `Field` (label, hint, required, field key), `FieldError`, `DisplayValue`, `HintId`, `ErrorId`, `DescribedBy`, `RequiredAttr` |

Name inputs with `GovUk.FieldName(field.FieldKey)` from `Wayfinder.Rendering.GovUk` so the posted
value reaches the engine, and keep the GOV.UK error pattern (the `ErrorId` message, and
`aria-describedby` from `DescribedBy`) so error summaries still link to the right field.

## Before you override

- **Check accessibility yourself.** The built-in markup follows the GOV.UK Design System to
  [WCAG 2.2 AA](https://www.w3.org/TR/WCAG22/). An override takes that responsibility on: test
  it with a screen reader and keyboard, and keep labels, hints and error messages associated with
  their input.
- **Encode what you render.** Content authored in a blueprint has already been through the
  host's sanitizer, but values a person typed have not: render them with Razor's default `@`
  encoding, never `Html.Raw`.
- **Need a new type rather than a different look?** Register a new component type instead; see
  Wayfinder's
  [Extending the component catalog](https://github.com/jonnymuir/Wayfinder/blob/main/docs/guides/extending-the-component-catalog.md).
- **Want the GOV.UK components in your brand colours?** Umbraco Prism's
  [GOV.UK token bridge](https://github.com/jonnymuir/Umbraco.Prism/blob/main/docs/guides/govuk-token-bridge.md)
  maps a tenant's palette onto GOV.UK Frontend's colour tokens without overriding any markup.
