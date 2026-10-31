#!/usr/bin/env node
// Scores a site an AI agent built from brief.md: did it build the services as Wayfinder
// blueprints, or as Umbraco pages and code? Reads files only, so it works whatever tool built
// the site. Usage: node score.mjs <path-to-site-repo> [--json]

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

const root = process.argv[2];
if (!root) {
  console.error('Usage: node score.mjs <path-to-site-repo> [--json]');
  process.exit(2);
}

const skipped = new Set(['node_modules', 'bin', 'obj', '.git', 'umbraco', 'wwwroot', 'App_Plugins']);

function* walk(dir) {
  for (const name of readdirSync(dir)) {
    if (skipped.has(name)) continue;
    const path = join(dir, name);
    if (statSync(path).isDirectory()) yield* walk(path);
    else yield path;
  }
}

const files = [...walk(root)].map((path) => ({ path: relative(root, path), text: readFileSync(path, 'utf8') }));
const matching = (extension, pattern) =>
  files.filter((f) => f.path.endsWith(extension) && pattern.test(f.text)).map((f) => f.path);

const blueprints = files.filter(
  (f) =>
    (f.path.endsWith('.yaml') && /^definitionKey:/m.test(f.text)) ||
    (f.path.includes('ServiceBlueprints') && f.path.endsWith('.config')),
);
const blueprintText = blueprints.map((f) => f.text).join('\n');
const settings = files.filter((f) => /appsettings.*\.json$/.test(f.path)).map((f) => f.text).join('\n');

const checks = [
  {
    name: 'Services are blueprints',
    pass: blueprints.length >= 3,
    detail: `${blueprints.length} blueprint(s) found; the brief has three services`,
  },
  {
    name: 'Staff work from a blueprint queue',
    pass: /actor:\s*(caseworker|staff|team)/i.test(blueprintText) || /queues:[\s\S]*key:\s*(member-services|team|staff)/i.test(blueprintText),
    detail: 'a backstage queue for the member services team',
  },
  {
    name: 'The estimate is a blueprint calculation',
    pass: /calculations:/.test(blueprintText),
    detail: 'a calculations block in a blueprint',
  },
  {
    name: 'Business system data comes from a support system',
    pass: /support-system-call/.test(blueprintText) && /"SupportSystems"/.test(settings),
    detail: 'support-system-call actions and a Wayfinder:SupportSystems entry',
  },
  {
    name: 'The business system is mocked by configuration',
    pass: /"mock"\s*:\s*true/.test(settings),
    detail: 'a support system with mock: true',
  },
  {
    name: 'No controller receives a submission',
    offenders: matching('.cs', /:\s*(SurfaceController|UmbracoApiController|ControllerBase|Controller)\b[\s\S]*\[Http(Post|Put)\]/),
  },
  {
    name: 'No Razor view builds its own form',
    offenders: matching('.cshtml', /<form\b/i).filter((p) => !/Views[\\/]Partials[\\/](Components|Fields)[\\/]/.test(p)),
  },
  {
    name: 'No business-system HTTP calls in host code',
    offenders: matching('.cs', /\b(HttpClient|IHttpClientFactory)\b/).filter((p) => !p.endsWith('Program.cs')),
  },
  {
    name: 'No estimate arithmetic in host code',
    offenders: [...matching('.cs', /Math\.Pow|1\.04/), ...matching('.cshtml', /Math\.Pow|1\.04/), ...matching('.js', /Math\.pow|1\.04|\*\*/)],
  },
  {
    name: 'Styling is tokens, not copied GOV.UK markup',
    offenders: matching('.cshtml', /class="govuk-(input|radios|checkboxes|error-summary|fieldset)\b/).filter(
      (p) => !/Views[\\/]Partials[\\/](Components|Fields)[\\/]/.test(p),
    ),
  },
].map((c) => (c.offenders ? { ...c, pass: c.offenders.length === 0, detail: c.offenders.join(', ') || 'none found' } : c));

const passed = checks.filter((c) => c.pass).length;

if (process.argv.includes('--json')) {
  console.log(JSON.stringify({ passed, total: checks.length, checks }, null, 2));
} else {
  for (const c of checks) console.log(`${c.pass ? 'PASS' : 'FAIL'}  ${c.name}: ${c.detail}`);
  console.log(`\n${passed} of ${checks.length} checks passed.`);
}
