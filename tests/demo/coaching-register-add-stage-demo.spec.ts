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
  waitForPromptText, waitForPromptTextGone, captureTerminal, configureTerminalSession, ansiToHtml
} from 'wayfinder-demo-recording-kit';
import { compressDeadTime } from 'wayfinder-demo-recording-kit/compress-dead-time';
import { parse } from 'yaml';

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
// (see README.md for the full operator setup). Mirrors Demo 3's Claude Code fallback in
// docs/demos/service-design-meetup-talk.md (Umbraco.Prism), Beats A-D: a small, live
// conversational edit to the same njf-coaching-register definition the Automate demo runs, the
// canvas following the agent's save, then a note pinned on the new stage that the agent works
// through. The bigger "design an entire service" capability is what mcp-authoring-demo.spec.ts
// records instead.

const adminCredentials = { email: 'admin@example.test', password: 'Wayfinder123!' };
const mcpAgentClientId = 'wayfinder-demo-agent';
const mcpAgentClientSecret = 'DemoAgentLocal!12345';
const definitionKey = 'njf-coaching-register';
const editorPath = `/umbraco/section/settings/workspace/wayfinder-service-blueprint/edit/${definitionKey}`;
// Plain http on purpose, as in the talk's fallback: the claude CLI does not trust the .NET dev
// certificate, and the OAuth login works the same over loopback http.
const mcpUrl = 'http://localhost:5299/wayfinder/service-blueprint-authoring/mcp';
const designNote = 'Make the nicknames sillier, and add one for diabolo players.';
const terminalSocket = 'wayfinder-umbraco-demo';
const terminalSession = 'wayfinder-umbraco-demo-terminal';
configureTerminalSession(terminalSocket, terminalSession);

// Beats C and D need the canvas and the agent on screen together, so the room sees the canvas
// move on its own when the agent saves. The kit's terminal mirror takes over the whole page, so
// this narrows the backoffice to the left and mirrors a narrower tmux pane on the right.
const splitTerminalWidth = 860;
const splitTerminalCols = 88;
const splitTerminalRows = 46;
let splitTimer: ReturnType<typeof setInterval> | null = null;

async function showSplitTerminal(page: Page): Promise<void> {
  execFileSync('tmux', ['-L', terminalSocket, 'resize-window', '-t', terminalSession,
    '-x', String(splitTerminalCols), '-y', String(splitTerminalRows)]);
  await page.evaluate(width => {
    const left = `${window.innerWidth - width}px`;
    document.documentElement.style.width = left;
    document.body.style.width = left;
    const panel = document.createElement('div');
    panel.id = 'demo-split-terminal';
    Object.assign(panel.style, {
      position: 'fixed', top: '0', right: '0', width: `${width}px`, height: '100vh',
      background: '#0d1117', zIndex: '2147483000', boxSizing: 'border-box', padding: '18px 20px',
      borderLeft: '2px solid #30363d', overflow: 'hidden'
    });
    const content = document.createElement('pre');
    content.id = 'demo-split-terminal-content';
    Object.assign(content.style, {
      margin: '0', color: '#e6edf3', whiteSpace: 'pre',
      font: '400 15px/21px "SF Mono", ui-monospace, Menlo, Consolas, monospace'
    });
    panel.appendChild(content);
    document.body.appendChild(panel);
    window.dispatchEvent(new Event('resize'));
  }, splitTerminalWidth);

  let busy = false;
  let lastFrame = '';
  splitTimer = setInterval(() => {
    if (busy) return;
    busy = true;
    void (async () => {
      try {
        const raw = captureTerminal();
        if (raw !== lastFrame) {
          lastFrame = raw;
          const html = ansiToHtml(raw.replace(/\n$/, ''));
          await page.evaluate(html => {
            const content = document.getElementById('demo-split-terminal-content');
            if (content) content.innerHTML = html;
          }, html);
        }
      } catch {
        // A capture hiccup, the next tick catches up.
      }
      busy = false;
    })();
  }, 300);
}

async function hideSplitTerminal(page: Page): Promise<void> {
  if (splitTimer) clearInterval(splitTimer);
  splitTimer = null;
  await page.evaluate(() => {
    document.getElementById('demo-split-terminal')?.remove();
    document.documentElement.style.width = '';
    document.body.style.width = '';
    window.dispatchEvent(new Event('resize'));
  }).catch(() => {});
}
const claudeSessionLogPath = '/tmp/wayfinder-umbraco-add-stage-demo-claude-session.log';
// Deliberately outside this repo, same reasoning as mcp-authoring-demo.spec.ts's own scratchDir:
// the whole point is proving the agent has no filesystem access to the codebase, only the MCP
// tools it was just given.
const scratchDir = path.join(tmpdir(), 'wayfinder-umbraco-add-stage-demo-scratch');
mkdirSync(scratchDir, { recursive: true });

test.describe.serial('Wayfinder.Umbraco coaching-register add-a-stage demo', () => {
  let page: Page;

  let newStageKey = '';

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

  async function readDefinition(token: string): Promise<{ stages?: Array<{ stageKey: string }> } | null> {
    const resp = await page.request.get(
      `/umbraco/management/api/v1/wayfinder/service-blueprints/${definitionKey}`,
      { headers: { Authorization: `Bearer ${token}` }, ignoreHTTPSErrors: true }
    ).catch(() => null);
    if (!resp?.ok()) return null;
    return parse(await resp.text());
  }

  // A freshly launched interactive agent session hits its own "MCP server needs authentication"
  // gate on its first real tool call, even after `claude mcp login`, and can ask again if an
  // authorization code expires. The URL comes from the session log's OSC 8 hyperlink target,
  // because the visible text is hard-wrapped. The sign-in runs in a side tab, so the recorded
  // page keeps the canvas on screen; its own video is discarded.
  const agentAuthorizeUrlPattern =
    /\x1b\]8;[^;]*;(https?:\/\/localhost:(?:44399|5299)\/umbraco\/management\/api\/v1\/security\/back-office\/authorize\?[^\x07\x1b]+)/g;
  let lastHandledAuthUrl = '';

  async function driveAgentAuthorizationIfWaiting(): Promise<void> {
    const logText = existsSync(claudeSessionLogPath) ? readFileSync(claudeSessionLogPath, 'utf8') : '';
    const matches = [...logText.matchAll(agentAuthorizeUrlPattern)].map(m => m[1]);
    if (!matches.length) return;
    const authUrl = matches[matches.length - 1];
    if (authUrl === lastHandledAuthUrl) return;
    lastHandledAuthUrl = authUrl;

    await beat(page, 'note', "The agent's first tool call asks for the same backoffice sign-in once more.", { position: 'top' });
    const authPage = await page.context().newPage();
    const callbackRequest = authPage
      .waitForRequest(r => r.url().startsWith('http://localhost:33418/callback'), { timeout: 30_000 })
      .catch(() => null);
    await authPage.goto(authUrl, { waitUntil: 'commit' }).catch(() => {});
    const raced = await Promise.race([
      callbackRequest.then(r => ({ pending: false as const, req: r })),
      authPage.waitForTimeout(2_000).then(() => ({ pending: true as const, req: null }))
    ]);
    let callbackReq = raced.req;
    if (raced.pending) {
      if (await authPage.locator('#username-input').isVisible({ timeout: 4_000 }).catch(() => false)) {
        await authPage.locator('#username-input').fill(adminCredentials.email);
        await authPage.locator('#password-input').fill(adminCredentials.password);
        await authPage.getByRole('button', { name: /login/i }).first().click();
      }
      const consent = authPage.getByRole('button', { name: /allow|authori[sz]e|accept|continue|grant|^yes/i }).first();
      if (await consent.isVisible({ timeout: 6_000 }).catch(() => false)) {
        await consent.click();
      }
      callbackReq = await callbackRequest;
    }
    const sideVideo = authPage.video();
    await authPage.close();
    await sideVideo?.delete().catch(() => {});
    await page.bringToFront();

    const redirectUrl = callbackReq?.url() ?? '';
    if (redirectUrl) {
      await waitForPaneStable();
      await sendTerminalText(redirectUrl);
      sendTerminalKey('Enter');
      await page.waitForTimeout(1_500);
    } else {
      console.log('Mid-conversation authorization: no loopback callback captured from the authorize redirect.');
    }
  }

  // The save lands while the agent is usually still writing its wrap-up, so let the turn end
  // before moving on, and the recording never leaves it mid-sentence.
  async function waitForAgentTurnToEnd(): Promise<void> {
    const deadline = Date.now() + 90_000;
    while (Date.now() < deadline) {
      if (!captureTerminal().includes('esc to interrupt')) {
        await waitForPaneStable(2_000);
        if (!captureTerminal().includes('esc to interrupt')) return;
      }
      await page.waitForTimeout(2_000);
    }
  }


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
    if (splitTimer) clearInterval(splitTimer);
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
        'already watched work end to end, watch it land on the canvas, then point at it and ask ' +
        'for a change.',
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
    await sendTerminalText('export BROWSER=true');
    sendTerminalKey('Enter');
    await waitForPaneStable();

    const authorizeUrlPattern =
      /https?:\/\/localhost:(?:44399|5299)\/umbraco\/management\/api\/v1\/security\/back-office\/authorize\?\S+/g;
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

    // The canvas goes on screen first and is never reloaded from here on, so everything it shows
    // after the agent saves is the editor following the change by itself.
    stopTerminalMirror();
    await page.goto(editorPath);
    await expect(page.locator('[data-wayfinder-stage="apply"]')).toBeVisible({ timeout: 30_000 });
    await showSplitTerminal(page);
    await page.waitForTimeout(800);
    await humanClick(page, page.getByRole('button', { name: 'Fit to screen' }));
    await beat(page, 'note', "The canvas on the left, the agent on the right. Nobody touches the canvas from here.", { position: 'top' });

    // Act 1 only registered and authenticated the MCP server, it never started an interactive
    // claude session to actually use it. Launch one now, same restricted-tools/bypass shape as
    // mcp-authoring-demo.spec.ts's own Act 2, before sending anything to the pane.
    await waitForPaneStable();
    await sendTerminalText('clear');
    sendTerminalKey('Enter');
    await waitForPaneStable();
    await sendTerminalText(
      'claude --model sonnet ' +
        '--tools "mcp__wayfinder-umbraco__*,ListMcpResourcesTool,ReadMcpResourceDirTool,ReadMcpResourceTool" ' +
        '--permission-mode bypassPermissions'
    );
    sendTerminalKey('Enter');

    // Two one-time consent gates can appear on a genuinely fresh scratch-directory launch, in
    // order: workspace trust, then BypassPermissions. Neither appears on every Claude Code
    // version. The trust gate is an arrow-selectable list with "No, exit" first, so Down then
    // Enter is what accepts it; a digit does nothing there.
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

    // Typing free text into an open selection menu corrupts the whole session.
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

    const initialToken = await mintAgentToken();
    if (!initialToken) throw new Error('Could not mint the demo agent token to read the starting definition.');
    const initial = await readDefinition(initialToken);
    const initialStageKeys = new Set((initial?.stages ?? []).map(stage => stage.stageKey));
    await expect.poll(
      async () => {
        await driveAgentAuthorizationIfWaiting();
        const token = await mintAgentToken();
        if (!token) return false;
        const definition = await readDefinition(token);
        newStageKey = (definition?.stages ?? []).map(stage => stage.stageKey).find(key => !initialStageKeys.has(key)) ?? '';
        return newStageKey !== '';
      },
      { timeout: 12 * 60_000, intervals: [5_000] }
    ).toBe(true);

    await waitForAgentTurnToEnd();
    await markWaitEnd();
    await hideFastForwardChip(page);

    await expect(page.getByText(/Updated elsewhere, from version/)).toBeVisible({ timeout: 60_000 });
    await humanClick(page, page.getByRole('button', { name: 'Fit to screen' }));
    await expect(page.locator(`[data-wayfinder-stage="${newStageKey}"]`)).toBeVisible({ timeout: 15_000 });

    await beat(
      page,
      'recap',
      'Nobody clicked reload. The canvas followed the agent to the new version and highlights what ' +
        'changed. It read the live definition as its style reference, added one stage in the right ' +
        'place, and validated and simulated before it saved.',
      { position: 'top' }
    );
  });

  test('Act 3, point at it', async () => {
    test.setTimeout(15 * 60_000);

    const newStage = page.locator(`[data-wayfinder-stage="${newStageKey}"]`);
    await beat(page, 'intent', "Now I don't describe what's wrong in prose. I point at it.", { position: 'top' });
    await humanClick(page, newStage);
    const selectionNotes = page.locator('[data-wayfinder-selection-notes]');
    if ((await selectionNotes.getAttribute('open')) === null) {
      await humanClick(page, selectionNotes.locator('summary'));
    }
    const noteForm = page.locator('[data-wayfinder-add-design-note]');
    await humanType(page, noteForm.getByRole('textbox'), designNote);
    await humanClick(page, noteForm.getByRole('button', { name: 'Add note' }));
    await expect(newStage).toHaveAttribute('data-wayfinder-stage-notes', '1', { timeout: 15_000 });
    await beat(page, 'note', 'A note marker on the stage itself.', { position: 'top' });

    await waitForPaneStable();
    await sendTerminalText(`Work through my notes on ${definitionKey}.`, 12);
    await page.waitForTimeout(300);
    sendTerminalKey('Enter');

    await markWaitStart('act3-notes');
    await showFastForwardChip(page, 'The agent is working through the note');
    await expect.poll(
      async () => {
        await driveAgentAuthorizationIfWaiting();
        return (await newStage.getAttribute('data-wayfinder-stage-notes')) === null;
      },
      { timeout: 12 * 60_000, intervals: [5_000] }
    ).toBe(true);
    await waitForAgentTurnToEnd();
    await markWaitEnd();
    await hideFastForwardChip(page);

    await beat(page, 'recap', 'The canvas moved to the next version again, and the marker cleared.', { position: 'top' });
    await humanClick(page, page.getByRole('tab', { name: /Notes/ }));
    await page.waitForTimeout(800);
    await humanClick(page, page.getByRole('button', { name: /^Resolved \(\d+\)$/ }));
    await expect(page.locator('[data-wayfinder-design-note-resolution]').first()).toBeVisible({ timeout: 15_000 });

    await beat(
      page,
      'recap',
      "The agent answers on the thing I pointed at, with what it did and the version it did it in. " +
        'If it needs a decision from me, it replies to the note with a question instead of guessing.',
      { position: 'top' }
    );
    await hideSplitTerminal(page);
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
