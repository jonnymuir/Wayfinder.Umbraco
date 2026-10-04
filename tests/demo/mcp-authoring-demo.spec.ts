import { test, type Page } from '@playwright/test';
import { clearSlate, showSlate } from 'wayfinder-demo-recording-kit';
import { ACT2_TIMEOUT_MS, runBrief } from './support/mcp-demo/act2-brief.js';
import { ACT5_TIMEOUT_MS, runTheService } from './support/mcp-demo/act5-run.js';
import { connectDesignPartner } from './support/mcp-demo/act1-connect.js';
import { publishToSite } from './support/mcp-demo/act3-publish.js';
import { showBlueprintInEditor } from './support/mcp-demo/act4-editor.js';
import { finishRecording, startRecording } from './support/mcp-demo/recording.js';

// Not a CI test — a demo-recording tool. Run with `npm run demo:record` from tests/demo (see
// README.md for the full operator setup: warm the reference app first, off-camera). One continuous
// take across every act, sharing a single Page; each act lives in support/mcp-demo/.

test.describe.serial('Wayfinder.Umbraco MCP authoring demo', () => {
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    page = await startRecording(browser);
  });

  test.afterAll(async () => {
    await finishRecording(page);
  });

  test('Cold open — introduce the demo', async () => {
    await showSlate(page, {
      eyebrow: 'WAYFINDER FOR UMBRACO',
      title: 'Design it. See it. Run it.',
      body:
        'A service blueprint is the shared picture of how a service works: the steps a person takes, ' +
        'the decisions behind the scenes, and the people who act on them. In the next few minutes a ' +
        'service designer describes one in plain language, an AI design partner turns it into a ' +
        'working blueprint through Wayfinder, and we publish it as a page in Umbraco and run it as ' +
        'an applicant and a caseworker.',
      holdMs: 14_000
    });
    await clearSlate(page);
  });

  test('Act 1 — connecting the design partner', async () => {
    await connectDesignPartner(page);
  });

  test('Act 2 — the brief', async ({ request }) => {
    test.setTimeout(ACT2_TIMEOUT_MS);
    await runBrief(page, request);
  });

  test('Act 3 — publishing it to the site', async () => {
    await publishToSite(page);
  });

  test('Act 4 — the blueprint in the visual editor', async () => {
    await showBlueprintInEditor(page);
  });

  test('Act 5 — running the service', async () => {
    test.setTimeout(ACT5_TIMEOUT_MS);
    await runTheService(page);
  });

  test('Closing slate', async () => {
    await showSlate(page, {
      eyebrow: 'WAYFINDER FOR UMBRACO',
      title: 'Design it. See it. Run it.',
      body:
        'A service designer described what they needed. An AI design partner built it with ' +
        "Wayfinder's authoring tools. Umbraco put it on the site, and real people used it. That is " +
        'the point of Wayfinder for Umbraco: good service design, made real on the platform your ' +
        'team already runs.'
    });
  });
});
