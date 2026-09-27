import { test, expect, type Page } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  beat, showSlate, clearSlate, moveNarrationTo, startNarrationTimeline, getNarrationTimeline,
  getWaitSegments, markWaitStart, markWaitEnd, showFastForwardChip, hideFastForwardChip,
  humanClick, humanType, startDemoTerminalSession, sendTerminalText, sendTerminalKey,
  showTerminalMirror, stopTerminalMirror, stripAnsiForMatching, waitForPaneStable,
  waitForPromptText, waitForPromptTextGone, captureTerminal
} from 'wayfinder-demo-recording-kit';
import { compressDeadTime } from 'wayfinder-demo-recording-kit/compress-dead-time';

// One shared Page across every act (see narrated-single-take-demo-recording skill). Playwright
// records one video per Page, so as long as nothing ever opens a second page, later acts are just
// later timestamps in the same file, not separate clips needing to be stitched together.
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
    console.log('ffmpeg not found on PATH, skipping the .mp4 convenience copy. The .webm is the real output.');
  }
}

// Not a CI test, a demo-recording tool. Run with `npm run demo:record:add-stage` from tests/demo
// (see README.md for the full operator setup). Mirrors Demo 3, Beats A-C of
// docs/demos/service-design-meetup-talk.md (Umbraco.Prism): a small, live conversational edit to
// the same njf-coaching-register definition the Automate demo runs, not a from-scratch build. The
// bigger "design an entire service" capability is what mcp-authoring-demo.spec.ts records instead.

const adminCredentials = { email: 'admin@example.test', password: 'Wayfinder123!' };
const mcpAgentClientId = 'wayfinder-demo-agent';
const mcpAgentClientSecret = 'DemoAgentLocal!12345';
const definitionKey = 'njf-coaching-register';
const claudeSessionLogPath = '/tmp/wayfinder-umbraco-add-stage-demo-claude-session.log';
// Deliberately outside this repo, same reasoning as mcp-authoring-demo.spec.ts's own scratchDir:
// the whole point is proving the agent has no filesystem access to the codebase, only the MCP
// tools it was just given.
const scratchDir = path.join(tmpdir(), 'wayfinder-umbraco-add-stage-demo-scratch');
mkdirSync(scratchDir, { recursive: true });

test.describe.serial('Wayfinder.Umbraco coaching-register add-a-stage demo', () => {
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
      const finalPath = path.join(footageDir, 'coaching-register-add-stage-demo.webm');
      await video.saveAs(finalPath);
      await video.delete();
      tryConvertToMp4(finalPath);
      writeFileSync(
        path.join(footageDir, 'coaching-register-add-stage-narration-timeline.json'),
        JSON.stringify(getNarrationTimeline(), null, 2)
      );
      const waitSegmentsPath = path.join(footageDir, 'coaching-register-add-stage-wait-segments.json');
      writeFileSync(waitSegmentsPath, JSON.stringify(getWaitSegments(), null, 2));

      const mp4Path = finalPath.replace(/\.webm$/, '.mp4');
      const sourceForCompression = existsSync(mp4Path) ? mp4Path : finalPath;
      const compressedPath = sourceForCompression.replace(/\.(mp4|webm)$/, '.compressed.mp4');
      try {
        const result = compressDeadTime(sourceForCompression, waitSegmentsPath, compressedPath);
        console.log(`Dead-time compression: ${JSON.stringify(result)}`);
      } catch (err) {
        console.error(`Dead-time compression failed, raw take is unaffected: ${err instanceof Error ? err.message : String(err)}`);
      }
    }
  });

  test('Cold open, introduce the demo', async () => {
    await showSlate(page, {
      eyebrow: 'WAYFINDER FOR UMBRACO',
      title: 'A service blueprint is something you can talk to',
      body:
        'Building an entire service from scratch with an AI design partner is one thing. Most ' +
        "days you're not doing that, you're changing something small on a service that's already " +
        "live. We'll ask an agent to add one new stage to the coaching-register blueprint we've " +
        'already watched work end to end, and see it land, live, in seconds.',
      holdMs: 12_000
    });
    await clearSlate(page);
  });

  test('Act 1, connecting the design partner', async () => {
    await beat(page, 'setup', 'Same backoffice, same login, same agent connection as always.');
    await page.goto('/umbraco/login');
    await humanType(page, page.getByLabel(/email/i), adminCredentials.email);
    await humanType(page, page.locator('#password-input'), adminCredentials.password);
    await humanClick(page, page.locator('button[type="submit"]').first());
    await page.waitForURL(url => !url.pathname.includes('/login'), { timeout: 30_000 });
    await page.waitForTimeout(1_000);

    await beat(
      page,
      'intent',
      'It signs in with the same backoffice login your editors use, so its permissions are ' +
        'exactly the permissions of the person who authorised it.'
    );
    await moveNarrationTo(page, 'top');

    await startDemoTerminalSession(claudeSessionLogPath, scratchDir);
    await showTerminalMirror(page);
    await page.waitForTimeout(800);

    await waitForPaneStable();
    await sendTerminalText('export NODE_TLS_REJECT_UNAUTHORIZED=0');
    sendTerminalKey('Enter');
    await waitForPaneStable();
    await sendTerminalText('export BROWSER=true');
    sendTerminalKey('Enter');
    await waitForPaneStable();

    const mcpUrl = 'https://localhost:44399/wayfinder/service-blueprint-authoring/mcp';
    const authorizeUrlPattern =
      /https:\/\/localhost:44399\/umbraco\/management\/api\/v1\/security\/back-office\/authorize\?\S+/g;
    const readSessionLog = (): string =>
      existsSync(claudeSessionLogPath) ? readFileSync(claudeSessionLogPath, 'utf8') : '';

    let mcpConnected = false;
    for (let attempt = 1; attempt <= 3 && !mcpConnected; attempt++) {
      await sendTerminalText(
        'claude mcp remove wayfinder-umbraco 2>/dev/null; ' +
          `claude mcp add --transport http wayfinder-umbraco ${mcpUrl} ` +
          '--client-id umbraco-back-office-wayfinder-mcp --callback-port 33418'
      );
      sendTerminalKey('Enter');
      await waitForPaneStable();

      const logOffset = readSessionLog().length;
      await sendTerminalText('claude mcp login wayfinder-umbraco --no-browser');
      sendTerminalKey('Enter');

      let authUrl = '';
      const urlDeadline = Date.now() + 30_000;
      while (Date.now() < urlDeadline && !authUrl) {
        const since = stripAnsiForMatching(readSessionLog().slice(logOffset));
        const matches = since.match(authorizeUrlPattern);
        if (matches?.length) authUrl = matches[matches.length - 1].replace(/[)\].,'"]+$/, '');
        else await page.waitForTimeout(500);
      }

      if (authUrl) {
        await beat(page, 'note', 'One approval, and the connection refreshes itself for the rest of the work.', { position: 'top' });
        const callbackRequest = page
          .waitForRequest(r => r.url().startsWith('http://localhost:33418/callback'), { timeout: 30_000 })
          .catch(() => null);
        await page.goto(authUrl, { waitUntil: 'commit' }).catch(() => {});

        const raced = await Promise.race([
          callbackRequest.then(r => ({ pending: false as const, req: r })),
          page.waitForTimeout(2_000).then(() => ({ pending: true as const, req: null }))
        ]);
        let callbackReq = raced.req;
        if (raced.pending) {
          if (await page.locator('#username-input').isVisible({ timeout: 4_000 }).catch(() => false)) {
            await humanType(page, page.locator('#username-input'), adminCredentials.email);
            await humanType(page, page.locator('#password-input'), adminCredentials.password);
            await humanClick(page, page.getByRole('button', { name: /login/i }).first());
          }
          const consent = page
            .getByRole('button', { name: /allow|authori[sz]e|accept|continue|grant|^yes/i })
            .first();
          if (await consent.isVisible({ timeout: 6_000 }).catch(() => false)) {
            await humanClick(page, consent);
          }
          callbackReq = await callbackRequest;
        }
        const redirectUrl = callbackReq?.url() ?? '';

        await showTerminalMirror(page);
        await page.waitForTimeout(500);
        await waitForPaneStable();
        if (redirectUrl) {
          await sendTerminalText(redirectUrl);
          sendTerminalKey('Enter');
          const authDeadline = Date.now() + 25_000;
          while (Date.now() < authDeadline) {
            const tail = stripAnsiForMatching(readSessionLog().slice(logOffset));
            if (/Authenticated with|tools are now available|Couldn't complete authentication/i.test(tail)) break;
            await page.waitForTimeout(500);
          }
          await page.waitForTimeout(1_000);
          sendTerminalKey('C-c');
          await waitForPaneStable();
          await sendTerminalText('clear');
          sendTerminalKey('Enter');
          await waitForPaneStable();
        } else {
          console.log(`MCP connection attempt ${attempt}: no loopback callback captured from the authorize redirect.`);
          sendTerminalKey('C-c');
          await page.waitForTimeout(1_000);
        }
      } else {
        console.log(`MCP connection attempt ${attempt}: no authorization URL appeared in the session log.`);
        sendTerminalKey('C-c');
      }

      await waitForPaneStable();
      await sendTerminalText('claude mcp list');
      sendTerminalKey('Enter');
      const listDeadline = Date.now() + 20_000;
      let listOutcome = '';
      while (Date.now() < listDeadline) {
        listOutcome = stripAnsiForMatching(captureTerminal());
        if (/wayfinder-umbraco.*(Connected|Failed to connect|[Nn]eeds authentication)/i.test(listOutcome)) break;
        await page.waitForTimeout(500);
      }
      mcpConnected = /wayfinder-umbraco.*Connected/i.test(listOutcome);
      if (!mcpConnected) {
        console.log(`MCP connection attempt ${attempt} did not report Connected: ${listOutcome.slice(-300)}`);
      }
    }
    if (!mcpConnected) {
      throw new Error('wayfinder-umbraco MCP server never reported Connected after 3 attempts, refusing to launch the recorded agent with no tools available.');
    }

    await beat(page, 'recap', "Connected. From here it's a designer, not a developer, just talking to it.", { position: 'top' });
  });

  test('Act 2, add a stage to a live blueprint', async () => {
    // A small, self-contained brief, no multi-turn back-and-forth expected, but real agent calls
    // doing a genuine validate/simulate loop have been observed elsewhere in this product line to
    // take longer than they look like they should, so this still budgets generously.
    test.setTimeout(15 * 60_000);

    await beat(
      page,
      'intent',
      "We'll ask it to change something small on a service that's already live, not build a new " +
        'one.',
      { position: 'top' }
    );

    const brief = [
      `In the ${definitionKey} definition, right after a coach is accredited, before the final `,
      'confirmation, add a stage where they pick a fun coaching nickname from a short list you ',
      'invent, juggling-themed, playful (think "The Whirling Dervish," "Captain Diabolo"). Show ',
      'their chosen nickname back to them on the confirmation screen. Validate and simulate the ',
      'accredited path, then save.'
    ].join('');

    await showSlate(page, {
      eyebrow: 'THE ASK',
      title: 'One small, real change',
      body: brief,
      bodyStyle: { whiteSpace: 'pre-wrap', textAlign: 'left', maxWidth: '980px', fontSize: '22px' }
    });
    await clearSlate(page);

    // Act 1 only registered and authenticated the MCP server, it never started an interactive
    // claude session to actually use it. Launch one now, same restricted-tools/bypass shape as
    // mcp-authoring-demo.spec.ts's own Act 2, before sending anything to the pane.
    await waitForPaneStable();
    await sendTerminalText(
      'claude --model sonnet ' +
        '--tools "mcp__wayfinder-umbraco__*,ListMcpResourcesTool,ReadMcpResourceDirTool,ReadMcpResourceTool" ' +
        '--permission-mode bypassPermissions'
    );
    sendTerminalKey('Enter');

    // Two one-time consent gates can appear on a genuinely fresh scratch-directory launch, in
    // order: workspace trust, then BypassPermissions. Neither appears on every Claude Code
    // version. Checking the LIVE pane (not a rolling log tail) before answering either. Confirmed
    // live that the trust-folder gate can take longer than a few seconds to render on a loaded
    // machine, an 8s window missed it entirely once and the brief got typed straight into the
    // still-open gate, so this budgets generously rather than tightly.
    //
    // Also confirmed live: this Claude Code version (2.1.283) renders the trust-folder gate as a
    // plain arrow-selectable list with no numbered options at all ("No, exit" highlighted first,
    // "Yes, I trust this folder" second), sending the digit "1" is not a valid input for it and
    // does nothing, which silently corrupted an earlier take. Down then Enter is what actually
    // moves the selection onto "Yes, I trust this folder" and confirms it.
    if (await waitForPromptText(/trust this folder/i, 20_000)) {
      await waitForPaneStable();
      sendTerminalKey('Down');
      await page.waitForTimeout(300);
      sendTerminalKey('Enter');
      await waitForPromptTextGone(/trust this folder/i, 8_000);
    }
    if (await waitForPromptText(/Yes, I accept/i, 10_000)) {
      await waitForPaneStable();
      await sendTerminalText('2');
      sendTerminalKey('Enter');
      await waitForPromptTextGone(/Yes, I accept/i, 8_000);
    }

    // Belt and braces: never send the brief while either gate is still visibly on screen, however
    // that happened. Typing free text into an open selection menu corrupts the whole session.
    const stillGated = () => /trust this folder|Yes, I accept/i.test(stripAnsiForMatching(captureTerminal()));
    for (let guard = 0; guard < 10 && stillGated(); guard++) {
      await page.waitForTimeout(1_000);
    }
    if (stillGated()) {
      throw new Error('A Claude Code consent gate is still showing after both checks, refusing to send the brief into it.');
    }

    await waitForPaneStable();
    await sendTerminalText(brief, 12);
    await page.waitForTimeout(300);
    sendTerminalKey('Enter');

    await markWaitStart('act2-design');
    await showFastForwardChip(page, 'The agent is making the change');

    // Confirmed live: claude mcp login (Act 1) authenticates the CLI's own meta-commands
    // (mcp add/list), but a freshly launched interactive agent session still hits its own,
    // separate "1 MCP server needs authentication" gate on its first real tool call, printing a
    // fresh authorize URL into the conversation and then waiting for a human to drive it. Missing
    // this once burned a full 12-minute poll budget for nothing.
    //
    // The URL must come from the session log's own OSC 8 hyperlink target, not the visually
    // wrapped text the TUI renders (confirmed live: the pane/log both hard-wrap a URL this long
    // at 150 columns as real line breaks, truncating a plain \S+ match at the first wrap; the
    // OSC 8 URI parameter itself carries the whole URL with no wrapping).
    //
    // Confirmed live this can happen more than once in the same conversation: an authorization
    // code can expire before the agent finishes processing it, and the agent then asks for a
    // fresh URL itself rather than getting stuck, so this re-triggers on any new, different
    // authorize URL rather than only ever handling the first one it sees.
    const authorizeUrlPattern =
      /\x1b\]8;[^;]*;(https:\/\/localhost:44399\/umbraco\/management\/api\/v1\/security\/back-office\/authorize\?[^\x07\x1b]+)/g;
    async function driveAuthorizeUrl(authUrl: string): Promise<string> {
      const callbackRequest = page
        .waitForRequest(r => r.url().startsWith('http://localhost:33418/callback'), { timeout: 30_000 })
        .catch(() => null);
      await page.goto(authUrl, { waitUntil: 'commit' }).catch(() => {});

      const raced = await Promise.race([
        callbackRequest.then(r => ({ pending: false as const, req: r })),
        page.waitForTimeout(2_000).then(() => ({ pending: true as const, req: null }))
      ]);
      let callbackReq = raced.req;
      if (raced.pending) {
        if (await page.locator('#username-input').isVisible({ timeout: 4_000 }).catch(() => false)) {
          await humanType(page, page.locator('#username-input'), adminCredentials.email);
          await humanType(page, page.locator('#password-input'), adminCredentials.password);
          await humanClick(page, page.getByRole('button', { name: /login/i }).first());
        }
        const consent = page
          .getByRole('button', { name: /allow|authori[sz]e|accept|continue|grant|^yes/i })
          .first();
        if (await consent.isVisible({ timeout: 6_000 }).catch(() => false)) {
          await humanClick(page, consent);
        }
        callbackReq = await callbackRequest;
      }
      return callbackReq?.url() ?? '';
    }

    let lastHandledAuthUrl = '';
    async function driveAgentAuthorizationIfWaiting(): Promise<void> {
      const logText = existsSync(claudeSessionLogPath) ? readFileSync(claudeSessionLogPath, 'utf8') : '';
      const matches = [...logText.matchAll(authorizeUrlPattern)].map(m => m[1]);
      if (!matches.length) return;
      const authUrl = matches[matches.length - 1];
      if (authUrl === lastHandledAuthUrl) return;
      lastHandledAuthUrl = authUrl;

      await beat(
        page,
        'note',
        "The agent's own first tool call needs a fresh authorization, same backoffice sign-in, " +
          'one more time.',
        { position: 'top' }
      );
      const redirectUrl = await driveAuthorizeUrl(authUrl);
      // The OAuth redirect chain (login -> consent -> loopback callback) can still be settling
      // its own navigation the instant driveAuthorizeUrl returns, confirmed live:
      // showTerminalMirror's own page.evaluate raced an in-flight navigation and threw "Execution
      // context was destroyed". A settle wait plus one retry covers it without guessing a fixed
      // delay that's either too short on a slow run or wasted time on a fast one.
      await page.waitForLoadState('load', { timeout: 5_000 }).catch(() => {});
      await page.waitForTimeout(500);
      try {
        await showTerminalMirror(page);
      } catch {
        await page.waitForTimeout(1_000);
        await showTerminalMirror(page);
      }
      await page.waitForTimeout(500);
      if (redirectUrl) {
        await waitForPaneStable();
        await sendTerminalText(redirectUrl);
        sendTerminalKey('Enter');
        await page.waitForTimeout(1_500);
      } else {
        console.log('Mid-conversation authorization: no loopback callback captured from the authorize redirect.');
      }
    }

    async function mintAgentToken(): Promise<string | null> {
      const resp = await page.request.post('/umbraco/management/api/v1/security/back-office/token', {
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

    async function readDefinition(token: string): Promise<{ stages?: unknown[] } | null> {
      const resp = await page.request.get(
        `/umbraco/management/api/v1/wayfinder/service-blueprints/${definitionKey}`,
        { headers: { Authorization: `Bearer ${token}` }, ignoreHTTPSErrors: true }
      ).catch(() => null);
      if (!resp?.ok()) return null;
      return resp.json();
    }

    const initialToken = await mintAgentToken();
    if (!initialToken) throw new Error('Could not mint the demo agent token to read the starting stage count.');
    const initial = await readDefinition(initialToken);
    const initialStageCount = initial?.stages?.length ?? 0;

    await expect.poll(
      async () => {
        await driveAgentAuthorizationIfWaiting();
        const token = await mintAgentToken();
        if (!token) return false;
        const definition = await readDefinition(token);
        return (definition?.stages?.length ?? 0) > initialStageCount;
      },
      { timeout: 12 * 60_000, intervals: [5_000] }
    ).toBe(true);

    // The save has landed, but the agent is usually still finishing its wrap-up message. Let its
    // turn actually end before cutting away, so the recording never leaves it visibly mid-sentence.
    const turnEndDeadline = Date.now() + 60_000;
    while (Date.now() < turnEndDeadline) {
      if (!captureTerminal().includes('esc to interrupt')) {
        await waitForPaneStable(2_000);
        if (!captureTerminal().includes('esc to interrupt')) break;
      }
      await page.waitForTimeout(2_000);
    }
    await markWaitEnd();
    await hideFastForwardChip(page);

    await beat(
      page,
      'recap',
      "Done, and it's already live. It read the existing definition as its own style reference, " +
        "added one stage in the right place, and validated and simulated before it saved. Same " +
        'discipline whether the change is one stage or twenty.',
      { position: 'top' }
    );
  });

  test('Act 3, see it in the graph', async () => {
    await beat(page, 'intent', "Let's see what it actually built, no restart, no redeploy.");

    await page.goto('/umbraco/section/settings');
    await page.waitForTimeout(1_200);
    await humanClick(page, page.getByText('Blueprints', { exact: true }));
    await page.waitForTimeout(1_200);
    await humanClick(page, page.getByRole('link', { name: 'Register as a juggling coach' }));
    await page.waitForTimeout(1_500);

    await humanClick(page, page.getByRole('button', { name: 'Fit to screen' }));
    await page.waitForTimeout(600);

    await beat(
      page,
      'recap',
      'That new stage is sitting right there in the graph, between the accredited outcome and ' +
        'confirmation. Nobody touched a line of code, and nothing in the brief named a stage, a ' +
        'component type, or where in the JSON it goes. That vocabulary is the agent\'s, from ' +
        'reading the live definition, not mine.'
    );
  });

  test('Closing slate', async () => {
    await showSlate(page, {
      eyebrow: 'WAYFINDER FOR UMBRACO',
      title: 'Talk to it, not just build with it',
      body:
        "That's the capability at its smallest: a real, live edit to a service that's already " +
        'running. The same conversation, given more space, builds the whole thing from nothing, ' +
        'the earlier recording in this same toolkit shows exactly that.'
    });
  });
});
