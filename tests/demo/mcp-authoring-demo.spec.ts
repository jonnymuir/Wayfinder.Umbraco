import { test, expect, type Page } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { beat, showSlate, clearSlate, moveNarrationTo, startNarrationTimeline, getNarrationTimeline } from './support/narration';
import { humanClick, humanType } from './support/human-interactions';
import {
  startDemoTerminalSession,
  sendTerminalText,
  sendTerminalKey,
  showTerminalMirror,
  stopTerminalMirror,
  stripAnsiForMatching,
  waitForPaneStable,
  captureTerminal
} from './support/tmux-terminal';

// One continuous take across every act, sharing a single Page created in beforeAll — Playwright
// records one video per Page, so as long as nothing ever opens a second page, "Act 5" is just a
// later timestamp in the same file as "Act 1", not a separate clip to stitch afterward. Ported
// structure from Umbraco.Prism's garden-waste-demo.spec.ts / tests/demo/README.md.
const __dirname = path.dirname(fileURLToPath(import.meta.url));
const footageDir = path.join(__dirname, 'demo-footage');
mkdirSync(footageDir, { recursive: true });

function tryConvertToMp4(webmPath: string): void {
  const mp4Path = webmPath.replace(/\.webm$/, '.mp4');
  try {
    execFileSync(
      'ffmpeg',
      ['-y', '-i', webmPath, '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-c:a', 'aac', mp4Path],
      { stdio: 'ignore' }
    );
    console.log(`Also wrote ${mp4Path}.`);
  } catch {
    console.log('ffmpeg not found on PATH — skipping the .mp4 convenience copy. The .webm is the real output.');
  }
}

// Not a CI test — a demo-recording tool. Run with `npm run demo:record` from tests/demo (see
// README.md for the full operator setup: warm the reference app first, off-camera).

const adminCredentials = { email: 'admin@example.test', password: 'Wayfinder123!' };
// Must match ReferenceMcpDemoAgentSeeder.ClientId/ClientSecret exactly — that seeder provisions
// the real user + credentials these constants exchange for a token; it has no env-var override
// of its own, so this can't safely diverge from it via environment either.
const mcpAgentClientId = 'wayfinder-demo-agent';
const mcpAgentClientSecret = 'DemoAgentLocal!12345';
const seededDefinitionKey = 'reference-demo';
const newDefinitionKey = 'transfer-juggling-licence';
const newDisplayName = 'Transfer a Professional Juggling Licence';
const claudeSessionLogPath = '/tmp/wayfinder-umbraco-demo-claude-session.log';
// Deliberately OUTSIDE this repo checkout — the whole point of Act 1/2 is proving the agent has
// no filesystem access to the codebase, only the MCP tools it was just given (--tools below
// enforces that regardless of cwd, but a scratch directory with no repo in reach keeps the
// framing honest, same convention as Umbraco.Prism's own tests/demo/README.md Act 4 setup).
const scratchDir = path.join(tmpdir(), 'wayfinder-umbraco-demo-scratch');
mkdirSync(scratchDir, { recursive: true });

// A minimal but genuinely valid one-page PDF — small enough to inline as a literal, real enough
// that a browser file-upload input and a server-side content-type check both accept it as a real
// PDF, not a text file wearing a .pdf extension.
const MINIMAL_PDF = Buffer.from(
  '%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n' +
    '2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n' +
    '3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\n' +
    'trailer<</Root 1 0 R>>\n%%EOF',
  'utf8'
);
const evidencePdfPath = path.join(scratchDir, 'juggling-licence-evidence.pdf');
writeFileSync(evidencePdfPath, MINIMAL_PDF);

test.describe.serial('Wayfinder.Umbraco MCP authoring demo', () => {
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    const recordingSize = { width: 1920, height: 1080 };
    const context = await browser.newContext({
      viewport: recordingSize,
      recordVideo: { dir: footageDir, size: recordingSize },
      ignoreHTTPSErrors: true
    });
    page = await context.newPage();
    startNarrationTimeline();
  });

  test.afterAll(async () => {
    stopTerminalMirror();
    const video = page?.video();
    await page?.close();
    if (video) {
      const finalPath = path.join(footageDir, 'wayfinder-umbraco-mcp-authoring-demo.webm');
      await video.saveAs(finalPath);
      await video.delete();
      tryConvertToMp4(finalPath);
      writeFileSync(
        path.join(footageDir, 'narration-timeline.json'),
        JSON.stringify(getNarrationTimeline(), null, 2)
      );
    }
  });

  test('Cold open — introduce the demo', async () => {
    await showSlate(page, {
      eyebrow: 'WAYFINDER FOR UMBRACO',
      title: 'Authoring a real public service over MCP',
      body:
        "We're going to give an AI agent real, backoffice-authenticated access to Wayfinder.Umbraco's " +
        'own MCP authoring toolkit — no open sandbox, no shortcut — and watch it design and save a ' +
        "complete branching service from a single brief. Then we'll wire it into the site, review what " +
        'it built in the visual editor, and run it end to end as a real citizen and a real caseworker.',
      holdMs: 14_000
    });
    await clearSlate(page);
  });

  test('Act 1 — getting the agent real access', async () => {
    // The agent's API user + client credentials are provisioned by ReferenceMcpDemoAgentSeeder
    // on app startup, NOT created live here via Management API calls — deliberately, not a
    // shortcut. The historical Umbraco.Prism MCP demo this one is modelled on used the exact same
    // "provisioned once, ahead of time, the same way any integration would be" framing. It also
    // sidesteps a genuine tooling limitation confirmed live in this session: this Playwright
    // version redacts the live Authorization header/token value to the literal string
    // "[redacted]" on every inspection API tried (request.headers(), headerValue(), even raw CDP
    // Network.requestWillBeSent) when the traffic is the *page's own* network activity — so this
    // script cannot safely capture the interactive backoffice SPA's own bearer token to make
    // authenticated Management API calls itself. See ReferenceMcpDemoAgentSeeder's own remarks.

    await beat(page, 'setup', "This is the Umbraco backoffice — Settings has a real Users section, same as any Umbraco site.");
    await page.goto('/umbraco/login');
    await humanType(page, page.getByLabel(/email/i), adminCredentials.email);
    await humanType(page, page.locator('#password-input'), adminCredentials.password);
    await humanClick(page, page.locator('button[type="submit"]').first());
    await page.waitForURL(url => !url.pathname.includes('/login'), { timeout: 30_000 });
    await page.waitForTimeout(1_000);

    await beat(
      page,
      'intent',
      "The agent already has a dedicated identity waiting for it — a real API user, in the admin " +
        'group, provisioned ahead of time the same way any real integration would be, not created ' +
        'on the fly as a special back door.'
    );

    await beat(
      page,
      'intent',
      "Now, in a real terminal, we'll exchange those credentials for a bearer token and connect " +
        'Claude to nothing but the MCP endpoint this app exposes.'
    );
    await moveNarrationTo(page, 'top');

    await startDemoTerminalSession(claudeSessionLogPath, scratchDir);
    await showTerminalMirror(page);
    await page.waitForTimeout(800);

    // set +H: interactive bash's history expansion treats a bare "!" as an event reference —
    // confirmed live, DemoAgentLocal!12345 (the real seeded secret) blew up an otherwise-correct
    // curl command with "event not found" the first time this ran, well before anything MCP- or
    // Wayfinder-specific was even reached. NODE_TLS_REJECT_UNAUTHORIZED=0: the Claude CLI's own
    // Node HTTP client doesn't consult the system/keychain trust store `dotnet dev-certs https
    // --trust` populates — confirmed live, curl -k and Playwright's ignoreHTTPSErrors were both
    // happy against this self-signed dev cert while `claude mcp list` still failed
    // UNABLE_TO_VERIFY_LEAF_SIGNATURE until this was set. Scoped to this one throwaway terminal
    // session talking only to localhost, not applied anywhere else.
    // Two separate commands, not one "a; b" line — sendTerminalText's own per-character ";"
    // escaping (needed so tmux send-keys itself doesn't swallow a bare ";", see that function's
    // own remarks) turns a literal ";" into "\;" on the wire, which bash then parses as an
    // escaped literal semicolon CHARACTER, not a command separator — so "set +H; export ..."
    // silently became one single malformed command and NODE_TLS_REJECT_UNAUTHORIZED was never
    // actually set. Confirmed live: this was the root cause of a real take where the agent's
    // `claude mcp add` call silently never persisted a server entry at all (empty mcpServers in
    // ~/.claude.json for the run's own project path) and every later tool call had nothing to
    // call — not an MCP or engine bug, a bug in this recording script's own terminal driving.
    // waitForPaneStable() before EVERY send in this sequence, not just the first — confirmed
    // live twice, on two different commands ("claude --model sonnet" right after session
    // creation, and separately "claude mcp add ..." right after an unrelated "claude mcp remove"
    // had just returned): a send-keys call made before the shell has actually redrawn its prompt
    // loses its leading character(s) with no visible error. See waitForPaneStable's own remarks.
    await waitForPaneStable();
    await sendTerminalText('set +H');
    sendTerminalKey('Enter');
    await waitForPaneStable();
    await sendTerminalText('export NODE_TLS_REJECT_UNAUTHORIZED=0');
    sendTerminalKey('Enter');
    await waitForPaneStable();

    const tokenCommand =
      `curl -sk -X POST https://localhost:44399/umbraco/management/api/v1/security/back-office/token ` +
      `-d grant_type=client_credentials -d client_id=umbraco-back-office-${mcpAgentClientId} ` +
      `-d client_secret=${mcpAgentClientSecret} -o /tmp/mcp-token.json && cat /tmp/mcp-token.json`;
    await sendTerminalText(tokenCommand);
    sendTerminalKey('Enter');
    await waitForPaneStable();

    // Hard verification gate, not a fixed wait: confirmed live this is load-bearing, not just
    // careful — a run once proceeded straight to launching the real (expensive, ~30-40 minute)
    // recorded agent process even though `claude mcp list` had just printed "No MCP servers
    // configured", because nothing actually checked its output before moving on. Retry the whole
    // remove/add/list sequence (with a freshly-minted token each time) rather than trusting a
    // single attempt, and fail the test outright — never launch the agent — if it still isn't
    // connected after real retries.
    let mcpConnected = false;
    for (let attempt = 1; attempt <= 3 && !mcpConnected; attempt++) {
      if (attempt > 1) {
        // Re-mint — the first token may be stale/consumed by a failed prior attempt.
        await sendTerminalText(tokenCommand);
        sendTerminalKey('Enter');
        await waitForPaneStable();
      }

      // Idempotent across takes/attempts — a previous registration (possibly against a
      // since-expired token) would otherwise short-circuit `add` with "already exists" and leave
      // a dead entry.
      await sendTerminalText('claude mcp remove wayfinder-umbraco 2>/dev/null');
      sendTerminalKey('Enter');
      await waitForPaneStable();

      await sendTerminalText(
        'claude mcp add --transport http wayfinder-umbraco ' +
          'https://localhost:44399/wayfinder/service-blueprint-authoring/mcp ' +
          '--header "Authorization: Bearer $(jq -r .access_token /tmp/mcp-token.json)"'
      );
      sendTerminalKey('Enter');
      await waitForPaneStable();

      await sendTerminalText('claude mcp list');
      sendTerminalKey('Enter');
      // "claude mcp list" does its own live health check (visibly takes a beat, per the "Checking
      // MCP server health…" line) — waitForPaneStable alone isn't a strong enough signal that the
      // check has actually finished, so poll for the real outcome text directly instead.
      const listDeadline = Date.now() + 15_000;
      let listOutcome = '';
      while (Date.now() < listDeadline) {
        listOutcome = stripAnsiForMatching(captureTerminal());
        if (/wayfinder-umbraco.*(Connected|Failed to connect|needs authentication)/i.test(listOutcome)) break;
        await page.waitForTimeout(500);
      }
      mcpConnected = /wayfinder-umbraco.*Connected/i.test(listOutcome);
      if (!mcpConnected) {
        console.log(`MCP connection attempt ${attempt} did not report Connected: ${listOutcome.slice(-300)}`);
      }
    }
    if (!mcpConnected) {
      throw new Error('wayfinder-umbraco MCP server never reported Connected after 3 attempts — refusing to launch the recorded agent with no tools available.');
    }

    await beat(
      page,
      'recap',
      "Connected — a real identity, a real short-lived token, a real MCP session. From here it " +
        "works exactly like giving a new team member their login.",
      { position: 'top' }
    );
  });

  test('Act 2 — handing over the brief', async ({ request }) => {
    // Real agent call, doing real iterative validate → fix → re-validate work — observed
    // elsewhere in this product line to need well over an initial short poll budget.
    test.setTimeout(40 * 60_000);

    // Rehearsal mode: a live agent call can't be cheaply re-run just to check a selector in Acts
    // 3-5, and burning 30+ minutes of real agent time for that would be wasteful — fake the
    // agent's end state instead (PUT the fixture directly via the same REST endpoint
    // save_service_blueprint itself calls) and skip straight to a short beat, so every other act
    // can still be validated for real against the live stack. Never set for the real take.
    if (process.env.DEMO_REHEARSAL === '1') {
      await beat(page, 'note', '[Rehearsal mode] Faking the agent\'s end state instead of a real call.');
      const fixture = JSON.parse(readFileSync(path.join(__dirname, 'support', 'rehearsal-fake-blueprint.json'), 'utf8'));
      let token: string | null = null;
      for (let i = 0; i < 10 && !token; i++) {
        const resp = await request.post('/umbraco/management/api/v1/security/back-office/token', {
          ignoreHTTPSErrors: true,
          form: {
            grant_type: 'client_credentials',
            client_id: `umbraco-back-office-${mcpAgentClientId}`,
            client_secret: mcpAgentClientSecret
          }
        });
        if (resp.ok()) token = (await resp.json()).access_token;
        else await page.waitForTimeout(1000);
      }
      if (!token) throw new Error('Rehearsal mode: could not mint a token to PUT the fake blueprint.');
      const putResp = await request.put(`/umbraco/management/api/v1/wayfinder/service-blueprints/${newDefinitionKey}`, {
        headers: { Authorization: `Bearer ${token}` },
        ignoreHTTPSErrors: true,
        data: fixture
      });
      expect(putResp.ok(), `rehearsal PUT of the fake blueprint failed: ${await putResp.text()}`).toBeTruthy();
      return;
    }

    await beat(
      page,
      'setup',
      'This is the Claude CLI, connected through nothing but the MCP toolkit this host exposes — no ' +
        'special access, no shortcuts.',
      { position: 'top' }
    );
    await beat(
      page,
      'intent',
      "We'll hand it one brief and watch it design a complete branching service — eligibility, real " +
        'document upload, a review and declaration — entirely on its own.',
      { position: 'top' }
    );

    // --tools restricts the *entire* available toolset (not an allow-list layered on the
    // default one) to just this MCP server's tools plus the built-in MCP-resource readers —
    // without it Claude Code's own Agent/Task tool stays available and has been observed
    // elsewhere in this product line to spontaneously delegate a call to a background sub-agent
    // fork that never returns. --permission-mode bypassPermissions is safe specifically because
    // --tools has already narrowed the whole session to those calls against this local dev
    // stack — --model sonnet pins the model so the agent doesn't inherit an unrelated personal
    // default. Haiku was tried here as a cost lever (this task is documented, validation-guided
    // MCP tool-calling, not open-ended coding, so it looked like a reasonable fit) but confirmed
    // live not to work in this specific restricted-tools sandbox: instead of calling its actual
    // MCP tools, it hallucinated raw <function_calls><invoke name="bash"> text and tried
    // non-existent commands (which wayfinder, find . -name "*.md") — tools it doesn't have, since
    // Bash isn't in --tools — then gave up and asked unanswerable clarifying questions. Sonnet
    // completed this exact task correctly on the first two real attempts.
    await waitForPaneStable();
    await sendTerminalText(
      'claude --model sonnet ' +
        '--tools "mcp__wayfinder-umbraco__*,ListMcpResourcesTool,ReadMcpResourceDirTool,ReadMcpResourceTool" ' +
        '--permission-mode bypassPermissions'
    );
    sendTerminalKey('Enter');
    await page.waitForTimeout(3_000);

    // Two distinct one-time consent gates can appear here, in order, on a genuinely fresh
    // scratch-directory launch — confirmed live, both showed up and neither is the other:
    // (1) the workspace-trust gate ("Is this a project you created or trust? ... 1. Yes, I trust
    // this folder  2. No, exit"), which fired first and was NOT caught by only checking for the
    // second gate's own text — the brief's own text then got typed straight into that still-open
    // menu and corrupted the underlying shell. (2) the BypassPermissions gate ("1. No, exit
    // 2. Yes, I accept"). Neither appears on every Claude Code version, and neither should be
    // answered blindly — check the session log for each gate's own distinct text first.
    let recentLog = stripAnsiForMatching(readFileSync(claudeSessionLogPath, 'utf8').slice(-8000));
    if (/trust this folder/i.test(recentLog)) {
      await waitForPaneStable();
      await sendTerminalText('1');
      sendTerminalKey('Enter');
      await page.waitForTimeout(2_000);
    }

    recentLog = stripAnsiForMatching(readFileSync(claudeSessionLogPath, 'utf8').slice(-8000));
    if (/Yes, I accept|No, exit/i.test(recentLog)) {
      await waitForPaneStable();
      await sendTerminalText('2');
      sendTerminalKey('Enter');
      await page.waitForTimeout(1_500);
    }

    const brief = [
      `You're designing a Wayfinder service blueprint for the National Juggling Authority. Task: `,
      `design and build "${newDisplayName}" (definitionKey: ${newDefinitionKey}) — for someone who `,
      `already holds a professional juggling licence from another juggling authority and wants to `,
      `transfer it. Read the authoring-guide resource for the contract shape, and use `,
      `read_service_blueprint on the existing "${seededDefinitionKey}" definition as your style `,
      `reference for this host's conventions (same citizen/caseworker queue keys). Call `,
      `list_component_types first and design only with component types it actually returns — `,
      `include a real file-upload component for supporting evidence, a summary-list reviewing what `,
      `the applicant entered, and a final declaration boolean field — put the declaration on its own `,
      `stage, separate from the summary-list's own stage (a real submission bug reproduced this `,
      `session: a boolean field never validates when it's a sibling of a summary-list component on `,
      `the same stage — a "check your answers" stage followed by a separate "declare and submit" `,
      `stage is both the workaround and, arguably, better GDS practice anyway). Branch on eligibility `,
      `(whether they already hold an existing licence) using showWhen — but put the boolean field that `,
      `showWhen branches on on its OWN stage, separate from the stage whose routes carry the showWhen `,
      `(another real bug reproduced this session: showWhen evaluates against PRE-submission state, so `,
      `a route can never correctly branch on a field captured in that very same submission — it always `,
      `takes the same branch regardless of the real answer; confirmed by actually running it as a `,
      `citizen and watching every answer land on the same outcome). So: one stage captures `,
      `hasExistingLicence, then an unconditional gateway hands off to a second, separate stage whose `,
      `two routes use showWhen on that already-captured field to branch for real. Remember every `,
      `stage route must target a gateway, never another stage directly, even a plain unconditional `,
      `hop, so each branch needs its own single-route pass-through gateway in between (check the `,
      `authoring-guide resource and the style reference's own route targets for exactly how that's `,
      `structured). Once you believe the design is right, don't just validate and simulate it — `,
      `actually call simulate_service_blueprint with real field values for BOTH branches (eligible `,
      `and not-eligible) and confirm each one truly lands on the outcome it should before saving; a `,
      `design that only validates cleanly can still be functionally wrong. Fix anything you find, `,
      `then save the service blueprint with displayName "${newDisplayName}".`
    ].join('');

    await waitForPaneStable();
    await sendTerminalText(brief, 12);
    await page.waitForTimeout(300);
    sendTerminalKey('Enter');

    // The real completion signal is the saved definition itself, not anything printed in the
    // terminal — poll the plain REST authoring API (not MCP) for the one fact that can only
    // become true via a real save_service_blueprint call reaching the live engine.
    // ServiceBlueprintAuthoringController is gated by BlueprintsAdmin, so this needs a bearer
    // token — minted fresh on every attempt (client-credentials tokens are short-lived, ~5
    // minutes, far shorter than this poll's own budget) using the same MCP agent credentials
    // from Act 1, rather than one token captured up front that would expire mid-poll.
    async function mintAgentToken(): Promise<string | null> {
      const resp = await request.post('/umbraco/management/api/v1/security/back-office/token', {
        ignoreHTTPSErrors: true,
        form: {
          grant_type: 'client_credentials',
          client_id: `umbraco-back-office-${mcpAgentClientId}`,
          client_secret: mcpAgentClientSecret
        }
      }).catch(() => null);
      if (!resp?.ok()) return null;
      const body = await resp.json();
      return body.access_token ?? null;
    }

    // The client-credentials token `claude mcp add` was given (Act 1) is short-lived (~5
    // minutes) and gets cached in-memory by the already-running `claude` process — it does NOT
    // pick up a refreshed value from ~/.claude.json on its own. Confirmed live, twice, in a real
    // take: the agent's own tool calls started failing with an auth error mid-design ("The MCP
    // connection needs re-authorization") once the original token aged out, and it correctly
    // stopped and asked for help rather than guessing. The real, working recovery (also
    // confirmed live): write a freshly-minted token into the stored config, then drive the
    // session's own `/mcp` → select server → Reconnect flow, which re-reads the config file —
    // after that a plain nudge lets the agent resume exactly where it left off. This loop does
    // that proactively, well inside the token's lifetime, so the agent ideally never needs to
    // stop and ask at all.
    const claudeConfigPath = path.join(homedir(), '.claude.json');
    async function refreshStoredMcpToken(): Promise<void> {
      const token = await mintAgentToken();
      if (!token) return;
      const config = JSON.parse(readFileSync(claudeConfigPath, 'utf8'));
      const project = config.projects?.[scratchDir];
      if (project?.mcpServers?.['wayfinder-umbraco']) {
        project.mcpServers['wayfinder-umbraco'].headers.Authorization = `Bearer ${token}`;
        writeFileSync(claudeConfigPath, JSON.stringify(config));
      }
    }

    async function reconnectMcpAndNudge(): Promise<void> {
      await waitForPaneStable();
      await sendTerminalText('/mcp');
      sendTerminalKey('Enter');
      await page.waitForTimeout(1_500);
      sendTerminalKey('Enter'); // select the (only) server row
      await page.waitForTimeout(1_000);
      sendTerminalKey('Enter'); // select "Reconnect" (first menu item)
      await page.waitForTimeout(1_500);
      await waitForPaneStable();
      await sendTerminalText('Reconnected. Please retry.');
      sendTerminalKey('Enter');
      await page.waitForTimeout(1_000);
    }

    let stopKeepalive = false;
    const keepalive = (async () => {
      const refreshEveryMs = 4 * 60_000;
      while (!stopKeepalive) {
        // Poll the stop flag in short slices rather than one long wait — the finally block
        // below needs this loop to exit promptly once the real poll resolves, not up to
        // refreshEveryMs late.
        for (let waited = 0; waited < refreshEveryMs && !stopKeepalive; waited += 5_000) {
          await page.waitForTimeout(5_000);
        }
        if (stopKeepalive) break;
        await refreshStoredMcpToken();
        const recentLog = stripAnsiForMatching(readFileSync(claudeSessionLogPath, 'utf8').slice(-4000));
        if (/re-?auth|token.{0,20}expir/i.test(recentLog)) {
          await reconnectMcpAndNudge();
        }
      }
    })();

    try {
      await expect.poll(
        async () => {
          const token = await mintAgentToken();
          if (!token) return false;
          const response = await request.get(
            `/umbraco/management/api/v1/wayfinder/service-blueprints/${newDefinitionKey}`,
            { headers: { Authorization: `Bearer ${token}` }, ignoreHTTPSErrors: true }
          ).catch(() => null);
          if (!response?.ok()) return false;
          const definition = await response.json();
          // Recursive: a real, well-structured design nests input fields inside a fieldset
          // container (GDS convention, e.g. grouping "Your details" separately from "Supporting
          // evidence") — confirmed live, a shallow top-level-only check never found the file
          // upload in an otherwise complete, correctly-saved design, so this poll could never
          // succeed no matter how long it waited.
          type AnyComponent = { type?: string; children?: AnyComponent[] };
          const hasFileUpload = (components: AnyComponent[] | undefined): boolean =>
            (components ?? []).some(c => c.type === 'file-upload' || hasFileUpload(c.children));
          return Boolean(
            definition.stages?.length > 1 &&
            definition.gateways?.length > 0 &&
            definition.stages?.some((s: { components?: AnyComponent[] }) => hasFileUpload(s.components))
          );
        },
        { timeout: 35 * 60_000, intervals: [10_000] }
      ).toBe(true);
    } finally {
      stopKeepalive = true;
      await keepalive;
    }

    await beat(
      page,
      'recap',
      'And there it is — read the style reference, designed a branching flow, wrote real document ' +
        'upload and a review step, validated it, fixed what it flagged, and saved it back to the ' +
        'live engine.',
      { position: 'top' }
    );
  });

  test('Act 3 — wiring it into the site', async () => {
    await beat(page, 'intent', "Let's point the site's own Apply page at the service the agent just built — no restart, no redeploy.");

    await page.goto('/umbraco/section/content');
    await page.waitForTimeout(1_500);
    await humanClick(page, page.getByText('Apply', { exact: true }).first());
    await page.waitForTimeout(1_500);

    await beat(page, 'setup', "This block is what renders /apply — right now it points at our seeded placeholder service.");
    await humanClick(page, page.locator('umb-ref-grid-block').first());
    await page.waitForTimeout(1_000);

    const keyField = page.getByLabel('Blueprint key');
    await expect(keyField).toHaveValue(seededDefinitionKey, { timeout: 10_000 });
    await humanType(page, keyField, newDefinitionKey);
    await humanClick(page, page.getByRole('button', { name: 'Update', exact: true }));
    await page.waitForTimeout(500);

    await humanClick(page, page.getByRole('button', { name: 'Save and publish', exact: true }));
    await expect(page.getByText(/published/i).first()).toBeVisible({ timeout: 15_000 });

    await beat(
      page,
      'recap',
      'One field, one publish, and the real /apply page now serves the service the agent designed.'
    );
  });

  test('Act 4 — reviewing what it built', async () => {
    await beat(page, 'intent', "Let's see what it actually built, in the same visual editor a human service designer uses.");

    await page.goto('/umbraco/section/settings');
    await page.waitForTimeout(1_500);
    await humanClick(page, page.getByText('Blueprints', { exact: true }));
    await page.waitForTimeout(1_500);
    await humanClick(page, page.getByRole('link', { name: newDisplayName }));

    // data-wayfinder-active-service-blueprint/-service-blueprint-loaded exist as string constants
    // in the bundle (grepped from source) but aren't actually written to the DOM as literal
    // attributes on this version — confirmed live via a real ARIA snapshot showing the editor
    // genuinely loaded (heading, toolbar, canvas, all real). The heading is a real, visible,
    // robust readiness signal instead.
    await expect(page.getByRole('heading', { name: newDisplayName, level: 1 })).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('application', { name: /graph canvas/i })).toBeVisible({ timeout: 30_000 });

    await beat(page, 'setup', 'The full graph — every stage, gateway, and route the agent wrote.');
    await humanClick(page, page.getByRole('button', { name: 'Fit to screen' }));
    await page.waitForTimeout(600);

    await beat(
      page,
      'recap',
      'The eligibility branch, the document upload stage, and the review-and-declare flow — all ' +
        'there, all saved, all real.'
    );

    await humanClick(page, page.getByRole('tab', { name: /validation/i }));
    await page.waitForTimeout(800);
    await beat(page, 'note', 'And the validation tab confirms it — a clean, valid definition, exactly as the agent left it.');
  });

  test('Act 5 — running it end to end', async () => {
    // The default 5-minute config timeout isn't enough for a multi-step generic walk (up to 8
    // stages, each with real human-paced typing/clicks) plus the caseworker half of the act —
    // confirmed live, the default budget ran out mid-walk.
    test.setTimeout(10 * 60_000);

    await beat(page, 'setup', "Now let's be an actual applicant.");
    await page.goto('/demo/login');
    await humanClick(page, page.getByRole('button', { name: /Alex Applicant/i }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    await beat(page, 'intent', "We'll click through the real journey the agent designed — the way any applicant actually would.");
    await humanClick(page, page.getByRole('link', { name: 'Apply', exact: true }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    // The agent's own stage order/labels/field keys aren't fixed ahead of time — walk the journey
    // generically: on each stage, upload a file if a file input is present, tick any checkboxes
    // present (eligibility questions, the final declaration), fill any remaining required text
    // input with a plausible value, then submit via whichever button the form actually has.
    // Everything scoped to #main-content — confirmed live this is load-bearing, not just tidy: an
    // unscoped 'form button' selector matches the page shell's own Sign Out form (which precedes
    // main content in the DOM, see ReferenceAppPageShell.cs), silently logging the applicant out
    // mid-walk and stalling the rest of the act for its entire budget waiting on a "Sign out"
    // button that no longer exists once already signed out.
    const main = page.locator('#main-content');
    for (let stepGuard = 0; stepGuard < 8; stepGuard++) {
      await page.waitForTimeout(500);
      // Fill EVERY file input on the stage, not just the first — confirmed live, a real design
      // with two separate required uploads (e.g. licence evidence + proof of identity) silently
      // failed validation and re-displayed the same stage forever when only the first was filled,
      // exhausting the step budget with the request never actually reaching the caseworker queue.
      const fileInputs = main.locator('input[type="file"]');
      const fileInputCount = await fileInputs.count();
      if (fileInputCount > 0) {
        await beat(page, 'note', "Here's the real document upload the agent added — a genuine file, not a mocked step.");
        for (let i = 0; i < fileInputCount; i++) {
          await fileInputs.nth(i).setInputFiles(evidencePdfPath);
        }
        await page.waitForTimeout(600);
      }

      const checkboxes = main.locator('input[type="checkbox"]');
      const checkboxCount = await checkboxes.count();
      for (let i = 0; i < checkboxCount; i++) {
        const box = checkboxes.nth(i);
        if (!(await box.isChecked())) await humanClick(page, box);
      }

      // Wayfinder's "date" component renders as GOV.UK's real day/month/year triple-input
      // pattern (name="{fieldKey}-day" etc, all type="text") — NOT a native <input type="date">.
      // Confirmed live this must run BEFORE the generic text-fill pass below: an earlier attempt
      // let the generic pass match these (they genuinely are type="text") and stuff the same long
      // free-text value into a 2-digit day box, failing "must be a valid date" and silently
      // re-displaying the same stage forever. Filling these first, with real values, means the
      // generic pass below then skips them (its own empty-value check no longer matches).
      const dateFieldSuffixes: Array<[string, string]> = [['-day', '1'], ['-month', '1'], ['-year', '2020']];
      for (const [suffix, value] of dateFieldSuffixes) {
        const fields = main.locator(`input[name$="${suffix}"]`);
        const count = await fields.count();
        for (let i = 0; i < count; i++) {
          const field = fields.nth(i);
          if ((await field.inputValue().catch(() => '')) === '') {
            await humanType(page, field, value);
          }
        }
      }

      // A generic, plausible value per HTML5 input type — confirmed live this needs to be
      // comprehensive, not just plain text: a real design with an email field and a date field
      // (neither matching a text-only selector) silently failed validation and re-displayed the
      // same stage forever, exhausting the step budget with the request never actually reaching
      // the caseworker queue, the same failure mode the file-upload and Change-button fixes above
      // both hit for their own reasons.
      const typedFills: Array<[string, string]> = [
        ['input[type="text"]', 'Reference Juggling Authority, licence JGL-4471'],
        ['input:not([type])', 'Reference Juggling Authority, licence JGL-4471'],
        ['textarea', 'Reference Juggling Authority, licence JGL-4471'],
        ['input[type="email"]', 'alex@example.test'],
        ['input[type="tel"]', '07700 900123'],
        ['input[type="number"]', '1']
      ];
      for (const [selector, value] of typedFills) {
        const fields = main.locator(selector);
        const count = await fields.count();
        for (let i = 0; i < count; i++) {
          const field = fields.nth(i);
          if ((await field.inputValue().catch(() => '')) === '') {
            await humanType(page, field, value);
          }
        }
      }

      // Native <input type="date"> ignores/misinterprets literal keystroke typing of a
      // dash-separated string (segment-based input, locale-dependent) — .fill() sets the ISO
      // value directly and correctly instead, trading the humanized-typing flourish for a value
      // that's actually accepted.
      const dateInputs = main.locator('input[type="date"]');
      const dateCount = await dateInputs.count();
      for (let i = 0; i < dateCount; i++) {
        const field = dateInputs.nth(i);
        if ((await field.inputValue().catch(() => '')) === '') {
          await humanClick(page, field);
          await field.fill('2020-01-01');
        }
      }

      // Confirmed live this exclusion is load-bearing: a summary-list's own per-row "Change"
      // button is also a real <button> inside a <form>, and renders BEFORE the page's actual
      // continue/submit button — an unfiltered "first form button" pick clicked "Change" instead,
      // silently looping the walk back to an earlier stage over and over.
      const submit = main
        .locator('form button[type="submit"], form button')
        .filter({ hasNotText: /change/i })
        .first();
      if (await submit.count() === 0) break;
      await humanClick(page, submit);
      await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
      // Confirmed live this margin is load-bearing in headed mode (not just belt-and-braces):
      // checking main.locator('form').count() immediately after networkidle raced the DOM still
      // settling post-navigation, reading 0 forms and breaking the walk one stage early —
      // reproduced consistently in the full headed run, never in a faster headless script.
      await page.waitForTimeout(500);
      const stillHasForm = await main.locator('form').count();
      if (stillHasForm === 0) break;
    }

    await beat(page, 'recap', "Submitted — eligibility, evidence, review, and declaration, the exact flow the agent designed.");

    await beat(page, 'setup', "Now let's be the caseworker who picks it up.");
    await humanClick(page, page.getByRole('button', { name: 'Sign out', exact: true }).or(page.locator('button', { hasText: 'Sign out' })).first());
    await page.waitForTimeout(500);
    await page.goto('/demo/login');
    await humanClick(page, page.getByRole('button', { name: /Casey Caseworker/i }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    await beat(page, 'intent', "And there's the caseworker queue — the agent's own routing put this request right where it belongs.");
    await humanClick(page, page.getByRole('link', { name: 'Caseworker queue', exact: true }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    await expect(page.getByText(newDisplayName).first()).toBeVisible({ timeout: 15_000 });

    await beat(
      page,
      'recap',
      "A real request, in a real queue, ready to be picked up — built by an agent from one brief, " +
        'over nothing but a documented MCP toolkit.'
    );
  });

  test('Closing slate', async () => {
    await showSlate(page, {
      eyebrow: 'WAYFINDER FOR UMBRACO',
      title: "That's the whole loop",
      body:
        'A real backoffice identity, a real MCP connection, an AI-authored branching service — wired ' +
        'into the live site, reviewed in the visual editor, and run end to end by a real applicant ' +
        'and a real caseworker. Thanks for watching.'
    });
  });
});
