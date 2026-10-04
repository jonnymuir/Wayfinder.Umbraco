import { type APIRequestContext, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { demoDir, demoRun, mcpAgentClientId, mcpAgentClientSecret, seededDefinitionKey } from './demo-config.js';

const tokenEndpoint = '/umbraco/management/api/v1/security/back-office/token';
const blueprintsEndpoint = '/umbraco/management/api/v1/wayfinder/service-blueprints';

type AnyComponent = { type?: string; children?: AnyComponent[] };

/**
 * The real completion signal is the saved definition itself, not anything printed in the terminal —
 * these poll the plain REST authoring API (not MCP) for the one fact that can only become true via a
 * real save_service_blueprint call reaching the live engine. ServiceBlueprintAuthoringController is
 * gated by BlueprintsAdmin, so this needs a bearer token — minted fresh on every attempt
 * (client-credentials tokens are short-lived, ~5 minutes, far shorter than a poll's own budget)
 * using the same MCP agent credentials from Act 1, rather than one token captured up front that
 * would expire mid-poll.
 */
export async function mintAgentToken(request: APIRequestContext): Promise<string | null> {
  const resp = await request
    .post(tokenEndpoint, {
      ignoreHTTPSErrors: true,
      form: {
        grant_type: 'client_credentials',
        client_id: `umbraco-back-office-${mcpAgentClientId}`,
        client_secret: mcpAgentClientSecret
      }
    })
    .catch(() => null);
  if (!resp?.ok()) return null;
  const body = await resp.json();
  return body.access_token ?? null;
}

/**
 * The brief never told the agent what definitionKey or exact displayName to use — discover whatever
 * it actually chose instead of assuming a fixed key. The seeded "reference-demo" blueprint is the
 * only one known to exist at the start of this act, so the first OTHER entry the list endpoint
 * reports is the agent's own new creation, whatever it named it.
 */
async function discoverNewBlueprint(request: APIRequestContext, token: string): Promise<boolean> {
  const listResp = await request
    .get(blueprintsEndpoint, { headers: { Authorization: `Bearer ${token}` }, ignoreHTTPSErrors: true })
    .catch(() => null);
  if (!listResp?.ok()) return false;

  const summaries: Array<{ definitionKey: string; displayName: string }> = await listResp.json();
  const created = summaries.find(s => s.definitionKey !== seededDefinitionKey);
  if (!created) return false;
  demoRun.newDefinitionKey = created.definitionKey;
  demoRun.newDisplayName = created.displayName;
  return true;
}

/**
 * Recursive: a real, well-structured design nests input fields inside a fieldset container (GDS
 * convention, e.g. grouping "Your details" separately from "Supporting evidence") — confirmed live,
 * a shallow top-level-only check never found the file upload in an otherwise complete,
 * correctly-saved design, so a poll could never succeed no matter how long it waited.
 */
const hasFileUpload = (components: AnyComponent[] | undefined): boolean =>
  (components ?? []).some(c => c.type === 'file-upload' || hasFileUpload(c.children));

const isCompleteDesign = (definition: { stages?: Array<{ components?: AnyComponent[] }>; gateways?: unknown[] }): boolean =>
  Boolean(definition.stages && definition.stages.length > 1 && definition.gateways && definition.gateways.length > 0 && definition.stages.some(s => hasFileUpload(s.components)));

/** True once the agent's own blueprint has been saved with more than one stage, a gateway and a file upload. */
async function agentDesignIsSaved(request: APIRequestContext): Promise<boolean> {
  const token = await mintAgentToken(request);
  if (!token || (!demoRun.newDefinitionKey && !(await discoverNewBlueprint(request, token)))) {
    return false;
  }

  const response = await request
    .get(`${blueprintsEndpoint}/${demoRun.newDefinitionKey}`, { headers: { Authorization: `Bearer ${token}` }, ignoreHTTPSErrors: true })
    .catch(() => null);
  return response?.ok() ? isCompleteDesign(await response.json()) : false;
}

/** Waits (up to 55 minutes) for the agent to save a complete design to the live engine. */
export async function waitForAgentDesign(request: APIRequestContext): Promise<void> {
  await expect.poll(() => agentDesignIsSaved(request), { timeout: 55 * 60_000, intervals: [10_000] }).toBe(true);
}

/**
 * Rehearsal mode: a live agent call can't be cheaply re-run just to check a selector in Acts 3-5,
 * and burning 30+ minutes of real agent time for that would be wasteful — fake the agent's end
 * state instead (PUT the fixture directly via the same REST endpoint save_service_blueprint itself
 * calls) so every other act can still be validated for real against the live stack. Never for the
 * real take.
 */
export async function saveRehearsalBlueprint(request: APIRequestContext, wait: (ms: number) => Promise<void>): Promise<void> {
  const fixture = JSON.parse(readFileSync(path.join(demoDir, 'support', 'rehearsal-fake-blueprint.json'), 'utf8'));
  demoRun.newDefinitionKey = fixture.definitionKey;
  demoRun.newDisplayName = fixture.displayName;

  let token: string | null = null;
  for (let i = 0; i < 10 && !token; i++) {
    token = await mintAgentToken(request);
    if (!token) await wait(1000);
  }
  if (!token) throw new Error('Rehearsal mode: could not mint a token to PUT the fake blueprint.');

  const putResp = await request.put(`${blueprintsEndpoint}/${demoRun.newDefinitionKey}`, {
    headers: { Authorization: `Bearer ${token}` },
    ignoreHTTPSErrors: true,
    data: fixture
  });
  expect(putResp.ok(), `rehearsal PUT of the fake blueprint failed: ${await putResp.text()}`).toBeTruthy();
}
