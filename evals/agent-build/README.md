# Evaluation: does an agent build the Wayfinder way?

The guidance in this repo, in Wayfinder's guides, on the MCP endpoint and in
`Wayfinder.Umbraco.Templates` exists to steer an AI agent towards blueprints and away from Umbraco
pages and code. This evaluation checks that it does. Run it after changing any of that guidance,
and with each AI tool you care about; it does not depend on any one of them.

## Run it

1. Make a fresh site, as in the README's [Quickstart](../../README.md#quickstart), with the version
   of `Wayfinder.Umbraco` under test. For the "with guidance" run, also run
   `dotnet new wayfinder-agents` in it and connect the agent to the site's MCP endpoint.
2. Start a new agent session in the site's folder and give it [`brief.md`](brief.md) as the whole
   prompt. Answer its questions with "use your judgement", and let it build.
3. Export the blueprints to disk (uSync, **Settings → uSync → Export**) if the agent saved them
   through MCP rather than as files.
4. Score the result:

   ```bash
   node evals/agent-build/score.mjs path/to/site
   ```

## What it checks

The brief has three services (contact details, a question queue, a retirement estimate), member
data from a system with no API, and a look to apply. The scorer reads the site's files and checks
that the services are blueprints, staff work from a blueprint queue, the estimate is a blueprint
calculation, member data comes from a mocked support system, and that there are no controllers
receiving submissions, hand-built forms, `HttpClient` calls, estimate arithmetic or copied GOV.UK
markup in host code.

It is a smoke test, not a grade: read the agent's gap report and the site too. Compare a run with
the guidance against one without (skip step 1's extras) to see what the guidance changes.
