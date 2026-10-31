# Wayfinder for Umbraco: templates

`dotnet new` templates for sites built with
[Wayfinder for Umbraco](https://github.com/jonnymuir/Wayfinder.Umbraco).

```bash
dotnet new install Wayfinder.Umbraco.Templates
```

## `wayfinder-agents`

Run in the root of your site's repository:

```bash
dotnet new wayfinder-agents
```

It adds:

- **`AGENTS.md`**, the rules an AI coding agent needs to build this site the Wayfinder way: every
  form, step, decision, calculation, staff screen and call to a business system is a service
  blueprint, Umbraco content is only for pages that inform, and the look comes from design tokens.
  Most AI coding tools read `AGENTS.md`.
- **`CLAUDE.md`**, one line that imports `AGENTS.md`, for Claude Code.

If your repository already has a `CLAUDE.md`, `dotnet new` will not overwrite it. Add `--force`
to replace it, or add the line `@AGENTS.md` to your own.

The long form of these rules is
[Building a site with Wayfinder for Umbraco](https://github.com/jonnymuir/Wayfinder.Umbraco/blob/main/docs/building-a-site.md).
