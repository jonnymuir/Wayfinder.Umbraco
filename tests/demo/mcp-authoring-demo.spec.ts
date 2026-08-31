import { test, expect, type Page } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  beat,
  showSlate,
  clearSlate,
  moveNarrationTo,
  startNarrationTimeline,
  getNarrationTimeline,
  getWaitSegments,
  markWaitStart,
  markWaitEnd,
  showFastForwardChip,
  hideFastForwardChip
} from './support/narration';
import { humanClick, humanType } from './support/human-interactions';
import { compressDeadTime } from './support/compress-dead-time';
import {
  startDemoTerminalSession,
  sendTerminalText,
  sendTerminalKey,
  showTerminalMirror,
  stopTerminalMirror,
  stripAnsiForMatching,
  waitForPaneStable,
  waitForPromptText,
  waitForPromptTextGone,
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
// Deliberately NOT hardcoded — the brief (see Act 2) never tells the agent what definitionKey or
// exact displayName to use, on purpose: a real service designer wouldn't dictate an internal
// implementation slug. Act 2 discovers whatever the agent actually chose (the one new entry that
// appears in the blueprint list beyond the seeded one) and sets these for Acts 3/4 to read.
let newDefinitionKey = '';
let newDisplayName = '';
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
      const waitSegmentsPath = path.join(footageDir, 'wait-segments.json');
      writeFileSync(waitSegmentsPath, JSON.stringify(getWaitSegments(), null, 2));

      // Compress dead air (see compress-dead-time.ts) from the .mp4 if ffmpeg produced one, else
      // the raw .webm — a separate, clearly-named output file, never overwriting the raw take.
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
    // The agent authenticates by logging into the Umbraco backoffice — the same OAuth 2.1
    // (Authorization Code + PKCE) flow the backoffice's own login uses, against the public client
    // WayfinderMcpOAuthClientInstaller registers at startup. No API user to create, no token to
    // mint by hand. The one out-of-band thing this script still needs the seeded
    // ReferenceMcpDemoAgentSeeder credentials for is Act 2's REST poll for "has the agent saved
    // yet" — never for the demo's own narrative.

    await beat(page, 'setup', 'This is an ordinary Umbraco backoffice: the settings, content and users your team already works with every day.');
    await page.goto('/umbraco/login');
    await humanType(page, page.getByLabel(/email/i), adminCredentials.email);
    await humanType(page, page.locator('#password-input'), adminCredentials.password);
    await humanClick(page, page.locator('button[type="submit"]').first());
    await page.waitForURL(url => !url.pathname.includes('/login'), { timeout: 30_000 });
    await page.waitForTimeout(1_000);

    await beat(
      page,
      'intent',
      'We are going to connect an AI design partner to it. It signs in with the same backoffice ' +
        'login your editors use, so its permissions are exactly the permissions of the person who ' +
        'authorised it.'
    );

    await beat(
      page,
      'intent',
      'In a terminal, we point the design partner at one thing: the service-blueprint authoring ' +
        'tools this Umbraco site publishes over MCP.'
    );
    await moveNarrationTo(page, 'top');

    await startDemoTerminalSession(claudeSessionLogPath, scratchDir);
    await showTerminalMirror(page);
    await page.waitForTimeout(800);

    // NODE_TLS_REJECT_UNAUTHORIZED=0: the Claude CLI's own Node HTTP client doesn't consult the
    // system/keychain trust store `dotnet dev-certs https --trust` populates — confirmed live in
    // the earlier client-credentials version of this Act, `claude mcp list` failed
    // UNABLE_TO_VERIFY_LEAF_SIGNATURE against this self-signed dev cert until this was set.
    // BROWSER=true: `claude mcp add` for an OAuth-protected server would otherwise spawn the
    // system browser for the login; we drive that login in the *recorded* page instead (below),
    // from the authorization URL the CLI also prints. `true` is the /usr/bin/true no-op — the
    // conventional "open nothing" value. Both scoped to this one throwaway localhost session.
    // waitForPaneStable() before EVERY send, not just the first — a send-keys call made before
    // bash has redrawn its prompt silently loses leading characters (see that helper's remarks).
    await waitForPaneStable();
    await sendTerminalText('export NODE_TLS_REJECT_UNAUTHORIZED=0');
    sendTerminalKey('Enter');
    await waitForPaneStable();
    await sendTerminalText('export BROWSER=true');
    sendTerminalKey('Enter');
    await waitForPaneStable();

    const mcpUrl = 'https://localhost:44399/wayfinder/service-blueprint-authoring/mcp';

    // Hard verification gate, not a fixed wait: confirmed live in the earlier version of this Act
    // that this is load-bearing — a run once proceeded straight to launching the real (expensive,
    // 30-40 minute) recorded agent even though `claude mcp list` had just printed a failure,
    // because nothing checked its output. Retry the whole add/authorise/list sequence, and fail
    // the test outright — never launch the agent — if it still isn't connected after real retries.
    let mcpConnected = false;
    for (let attempt = 1; attempt <= 3 && !mcpConnected; attempt++) {
      // `remove` joined with `;` not `&&`: it exits 1 when there's nothing to remove (the normal
      // case on a fresh scratch dir), which `&&` would let short-circuit the whole line.
      await sendTerminalText(
        'claude mcp remove wayfinder-umbraco 2>/dev/null; ' +
          `claude mcp add --transport http wayfinder-umbraco ${mcpUrl} ` +
          '--client-id umbraco-back-office-wayfinder-mcp --callback-port 33418'
      );
      sendTerminalKey('Enter');

      // `claude mcp add` against this OAuth-protected endpoint prints an authorization URL and
      // starts a loopback listener on --callback-port. Pull that URL off the pane, drive the
      // backoffice consent in the recorded page, and let the CLI's own listener catch the
      // redirect and finish the token exchange. The admin session from the login above usually
      // carries the cookie straight through to a consent step; a login prompt is handled too in
      // case it doesn't.
      let authUrl = '';
      const urlDeadline = Date.now() + 25_000;
      while (Date.now() < urlDeadline && !authUrl) {
        const match = stripAnsiForMatching(captureTerminal()).match(
          /https:\/\/localhost:44399\/umbraco\/management\/api\/v1\/security\/back-office\/authorize\?\S+/
        );
        if (match) authUrl = match[0].replace(/[)\].,'"]+$/, '');
        else await page.waitForTimeout(500);
      }

      if (authUrl) {
        await beat(
          page,
          'note',
          'That is the standard backoffice sign-in. Approve it once, and the design partner has a ' +
            'session that refreshes itself for the rest of the work.',
          { position: 'top' }
        );
        await page.goto(authUrl);
        if (await page.locator('#username-input').isVisible({ timeout: 5_000 }).catch(() => false)) {
          await humanType(page, page.locator('#username-input'), adminCredentials.email);
          await humanType(page, page.locator('#password-input'), adminCredentials.password);
          await humanClick(page, page.getByRole('button', { name: /login/i }).first());
        }
        const consent = page
          .getByRole('button', { name: /allow|authori[sz]e|accept|continue|grant|^yes/i })
          .first();
        if (await consent.isVisible({ timeout: 8_000 }).catch(() => false)) {
          await humanClick(page, consent);
        }
        // Let the CLI's callback listener receive the code and complete before we look at the pane.
        await page.waitForTimeout(3_000);
        await showTerminalMirror(page);
        await page.waitForTimeout(800);
      } else {
        console.log(`MCP connection attempt ${attempt}: no authorization URL appeared on the pane.`);
      }

      await waitForPaneStable();
      await sendTerminalText('claude mcp list');
      sendTerminalKey('Enter');
      // `claude mcp list` runs its own live health check ("Checking MCP server health…") — poll
      // for the real outcome text rather than trusting waitForPaneStable alone.
      const listDeadline = Date.now() + 20_000;
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
      'The design partner is connected. From here it works like any colleague with a login: it can ' +
        'read and author service blueprints through the same tools a person would use.',
      { position: 'top' }
    );
  });

  test('Act 2 — the brief', async ({ request }) => {
    // Real agent call, doing real iterative validate → fix → re-validate work — observed
    // elsewhere in this product line to need well over an initial short poll budget. Confirmed
    // live: a genuinely good conversation that pauses to ask real clarifying questions (the whole
    // point of a domain-language brief, per the user's own direction) can legitimately run well
    // past 35 minutes once real dialogue back-and-forth is involved — a real take hit exactly that
    // 35-minute completion-poll ceiling while the agent was still productively waiting on an
    // answer, orphaning an otherwise-healthy conversation when the test gave up and exited.
    test.setTimeout(65 * 60_000);

    // Rehearsal mode: a live agent call can't be cheaply re-run just to check a selector in Acts
    // 3-5, and burning 30+ minutes of real agent time for that would be wasteful — fake the
    // agent's end state instead (PUT the fixture directly via the same REST endpoint
    // save_service_blueprint itself calls) and skip straight to a short beat, so every other act
    // can still be validated for real against the live stack. Never set for the real take.
    if (process.env.DEMO_REHEARSAL === '1') {
      await beat(page, 'note', '[Rehearsal mode] Faking the agent\'s end state instead of a real call.');
      const fixture = JSON.parse(readFileSync(path.join(__dirname, 'support', 'rehearsal-fake-blueprint.json'), 'utf8'));
      newDefinitionKey = fixture.definitionKey;
      newDisplayName = fixture.displayName;
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
      "This is the design partner. Everything it does from here goes through Wayfinder's " +
        'service-blueprint authoring tools.',
      { position: 'top' }
    );
    await beat(
      page,
      'intent',
      'A service designer at a licensing authority is about to describe a problem in their own ' +
        'words: the user need, the rules, and the standard they hold themselves to. We will watch ' +
        'the agent shape that into a service.',
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

    // Two distinct one-time consent gates can appear here, in order, on a genuinely fresh
    // scratch-directory launch — confirmed live, both showed up and neither is the other:
    // (1) the workspace-trust gate ("Is this a project you created or trust? ... 1. Yes, I trust
    // this folder  2. No, exit"), which fired first and was NOT caught by only checking for the
    // second gate's own text — the brief's own text then got typed straight into that still-open
    // menu and corrupted the underlying shell. (2) the BypassPermissions gate ("1. No, exit
    // 2. Yes, I accept"). Neither appears on every Claude Code version, and neither should be
    // answered blindly.
    //
    // Checking the LIVE pane (waitForPromptText, below) rather than a rolling session-log tail —
    // confirmed live this matters, not just tidier: a rolling buffer can still contain a gate's
    // own option text well after that gate was actually dismissed. Both gates' option text
    // includes "No, exit", so a log-tail match on that phrase couldn't distinguish "gate 1 still
    // showing" from "gate 1 already handled, and now just stale scrollback" — which once caused a
    // stray "2" to get sent straight into the live, ready prompt after both gates were long past.
    // Matching on "Yes, I accept" specifically (unique to gate 2 — gate 1's own affirmative option
    // reads "Yes, I trust this folder") removes the ambiguity entirely rather than working around
    // it, and waiting for each gate's text to actually disappear after answering confirms it was
    // really dismissed rather than just sent-and-hoped.
    if (await waitForPromptText(/trust this folder/i, 8_000)) {
      await waitForPaneStable();
      await sendTerminalText('1');
      sendTerminalKey('Enter');
      await waitForPromptTextGone(/trust this folder/i, 5_000);
    }

    if (await waitForPromptText(/Yes, I accept/i, 5_000)) {
      await waitForPaneStable();
      await sendTerminalText('2');
      sendTerminalKey('Enter');
      await waitForPromptTextGone(/Yes, I accept/i, 5_000);
    }

    // Pure domain language, no Wayfinder vocabulary anywhere — reviewed and approved by the real
    // user (kept em-dash-free to match the video's house style and the walkthrough doc's copy,
    // which must stay in sync with this). This is the entire point of the demo: a service designer
    // who has never heard
    // of Wayfinder describes a problem statement, user needs, and constraints in their own terms;
    // the MCP's own resources/skills/prompts are what teach the LLM the implementation mechanics
    // (routes, gateways, showWhen, component types), not this brief. Deliberately does NOT name a
    // definitionKey, an exact displayName, a style-reference blueprint, or which MCP tools/
    // resources to use — a real designer wouldn't know any of that exists, and specifying it here
    // would be feeding the agent an already-translated answer instead of proving the MCP does the
    // translating. It also does NOT pre-avoid the two real engine bugs found during earlier
    // debugging this session (boolean field validation as a summary-list sibling; showWhen
    // evaluating pre-submission state) — a real designer has no way to know either exists; if the
    // live agent hits one, that's the validation-feedback loop working as intended, on camera, not
    // a problem to engineer around in advance.
    const brief = [
      'Hi. I work on licensing for the National Juggling Authority. I need help designing a new service.\n\n',
      'The problem: right now, if someone already holds a current professional juggling licence from ',
      'another recognised juggling authority and wants to work here, they have to apply for a brand ',
      "new licence from scratch, exactly the same as someone who's never juggled professionally ",
      "before. That's not fair on them, it duplicates assessment work that's already been done ",
      'properly elsewhere, and it puts off exactly the experienced jugglers we want performing here.\n\n',
      'I want a "transfer your licence" service instead. What I know about how it needs to work:\n',
      '- Only for jugglers who already hold a current licence from a juggling authority we formally ',
      "recognise. Right now that's the European Juggling Federation, Async Circle International, ",
      "and the Ring Masters Guild. Anyone else isn't eligible for transfer; they need to apply as a ",
      'new licence holder instead, which is a separate existing service.\n',
      '- We need to see their current licence certificate and some proof of who they are.\n',
      '- Before we grant anything, they need to formally declare they\'ll uphold our professional ',
      'standards, the same declaration a new applicant makes.\n',
      '- A caseworker always has to check the evidence and make the actual decision. This can\'t be ',
      'auto-approved, someone has to look at the documents.\n',
      '- Same accessibility bar as everything else we ship: WCAG double-A, in line with the GDS ',
      'service standard.\n\n',
      "Can you help me design this properly? Ask me anything you need."
    ].join('');

    // A small, prepared set of in-character domain answers was tried first here and abandoned —
    // confirmed live it's the wrong shape entirely, not just imperfectly tuned: its topic-matching
    // regexes are coarse keyword matches, and a genuinely different question can innocently share a
    // keyword with an earlier, already-answered one (e.g. "does another system issue the new
    // certificate? Any new expiry date to show them?" re-matched an /expir/i entry meant for "has
    // their licence expired") — three separate dedup-keying schemes were tried to work around this
    // class of false positive, and all three still depended on regex matching being right in the
    // first place, which it fundamentally can't always be for open-ended real dialogue.
    //
    // Replaced with a real model call that generates the designer's answer fresh, in character, for
    // every question — no pattern matching, so there's no keyword-collision class of bug left to
    // have. Haiku, not Sonnet: this is a simple, well-specified conversational completion (answer
    // one question, in character, from a fixed brief) — a good fit, unlike the earlier abandoned
    // attempt to have Haiku drive the actual MCP-based design work itself (confirmed live to fail
    // there: it hallucinated tool calls outside its restricted --tools allowlist instead of using
    // its real MCP tools). No tools at all here (`--tools ""`) — pure text-in/text-out, matching
    // `-p`'s own non-interactive print-and-exit mode. The prompt positional argument must come
    // BEFORE `--tools` on the command line — confirmed live, `--tools` is a variadic flag
    // (`<tools...>`) that otherwise swallows the next argument as an additional (invalid) tool name
    // and leaves nothing for the prompt itself.
    async function generateDesignerAnswer(questionTail: string): Promise<string> {
      const systemPrompt =
        'You are roleplaying as a service designer at the National Juggling Authority, answering ' +
        "a software team's clarifying question about a service you commissioned. Answer ONLY in " +
        'plain domain language. You are not a software engineer and know nothing about how the ' +
        'underlying system is built — never use or reference implementation terms (routes, ' +
        'gateways, showWhen, JSON, component types, field keys, or anything like that). Keep it ' +
        'brief and conversational — 1-4 sentences, like a real chat reply. Stay consistent with ' +
        "the brief you already gave; if something wasn't specified, make a sensible judgment call " +
        'as the domain expert.';
      const userPrompt =
        `The brief you gave earlier:\n\n${brief}\n\n` +
        `The team's current question (this may include some surrounding conversation context — ` +
        `answer whatever they're actually asking now):\n\n${questionTail}`;

      const fallback =
        'Good question. Use your best judgement on that one, based on how the rest of the ' +
        'service works; I trust you to make a sensible call.';

      for (let attempt = 1; attempt <= 2; attempt++) {
        try {
          const raw = execFileSync(
            'claude',
            ['-p', '--model', 'haiku', '--system-prompt', systemPrompt, userPrompt, '--tools', ''],
            { encoding: 'utf8', timeout: 30_000 }
          );
          const trimmed = raw.trim();
          if (trimmed) return trimmed;
        } catch (err) {
          console.error(`generateDesignerAnswer attempt ${attempt} failed: ${err instanceof Error ? err.message : String(err)}`);
        }
      }
      return fallback; // both attempts failed — a hardcoded safety net, not the primary mechanism
    }

    // The brief is long enough that typing it into the bounded tmux pane scrolls earlier lines
    // away before a viewer ever sees the whole thing at once — a terminal-viewport problem, not an
    // animation-speed one, so a full-screen slate (immune to pane scrolling) shows the complete
    // text first, held for a genuinely reading-paced duration, before it's sent to the terminal at
    // all. bodyStyle overrides the slate's default centered/no-wrap styling with left-aligned
    // pre-wrap so the brief's own paragraph breaks and bullet list render as written.
    await showSlate(page, {
      eyebrow: 'THE BRIEF',
      title: "The brief, in the designer's words",
      body: brief,
      bodyStyle: { whiteSpace: 'pre-wrap', textAlign: 'left', maxWidth: '980px', fontSize: '22px' }
    });
    await clearSlate(page);

    await waitForPaneStable();
    await sendTerminalText(brief, 12);
    await page.waitForTimeout(300);
    sendTerminalKey('Enter');

    // Dead-air handling for this act. The agent's silent design stretches are the only genuine
    // dead air — nothing for a viewer to read, real wall-clock time on a live external process
    // that can't be sped up live. Those are marked as wait segments (compressed afterward, with a
    // visible "fast-forwarding" chip so the sped-up frames read as "moving through waiting time").
    // The clarifying-question exchanges are deliberately NOT marked: the designer's answer is
    // typed out at real speed, because that plain-language back-and-forth is the design work and
    // the part worth watching. enterWait/leaveWait keep the marker, the chip, and this local
    // mirror in lockstep so an exception mid-exchange can't leave a stretch unmarked.
    const DESIGN_CHIP = 'The agent is designing the blueprint';
    const ANSWER_CHIP = 'Waiting for the designer';
    let openWaitLabel: string | null = null;
    async function enterWait(label: string, chipText: string): Promise<void> {
      if (openWaitLabel) return;
      markWaitStart(label);
      openWaitLabel = label;
      await showFastForwardChip(page, chipText);
    }
    async function leaveWait(): Promise<void> {
      if (!openWaitLabel) return;
      markWaitEnd();
      openWaitLabel = null;
      await hideFastForwardChip(page);
    }
    await enterWait('act2-design', DESIGN_CHIP);

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

    // A proactive token-refresh + /mcp-Reconnect-and-nudge mechanism used to live here, on the
    // theory that refreshing well inside the token's ~30-minute lifetime meant the agent would
    // ideally never need to stop and ask. Removed entirely — confirmed live it was net-harmful,
    // not just unnecessary: reconnectMcpAndNudge sent "Reconnected. Please retry." unconditionally,
    // regardless of whether the /mcp reconnect actually succeeded. In one real take the reconnect
    // itself failed (401, "OAuth fallback is disabled when headers.Authorization is set"), the
    // false "Reconnected" claim sent the agent retrying into a connection with zero MCP tools
    // available — strictly worse than doing nothing — and it had to stop and ask for help anyway.
    // A genuine token expiry is already handled fine without any of this: the agent's own tool
    // calls fail with a real auth error and it correctly stops and asks rather than guessing, which
    // is a perfectly good outcome on its own. The token lifetime (confirmed live, ~30 minutes) is
    // also comfortably longer than a realistic Act 2 conversation now runs, making the proactive
    // path's original justification moot even before its own bug is considered.

    // Real dialogue, not a one-shot paste: the agent may ask genuine clarifying questions before
    // it's done designing, and the "designer" persona should answer in domain language, live, via
    // generateDesignerAnswer above — a real model call, not pattern matching, so there's no
    // keyword-collision class of bug to have any more (see that function's own remarks for the
    // three earlier, all pattern-matching-based approaches this replaced). Dedup is still needed
    // independently of that, though: keyed by the qualifying tail text itself, so an exact repeat
    // of the identical question (e.g. a stale re-render) doesn't get a second, possibly-differently
    // worded model-generated reply for no reason.
    const questionsAnswered = new Set<string>();
    let questionBeatsShown = 0;
    // Returns a short description of what happened this tick, for the keepalive loop to log —
    // every tick, not just errors, so a future stall leaves a real trail instead of silence either
    // way (see the keepalive loop's own remarks for why silence alone doesn't distinguish "nothing
    // to do" from "the whole mechanism died").
    async function respondToLiveQuestionIfWaiting(): Promise<string> {
      // "esc to interrupt" is Claude Code's own TUI state indicator — present ONLY while it's
      // actively working (a real tool call, or still composing a response), removed the instant
      // it's back at an idle prompt. Confirmed live (both a quick text-only reply and a real
      // multi-second tool call): this is a genuine UI state signal, not a text-content heuristic —
      // checking it FIRST, before anything content-based, means this can never send while Claude is
      // still genuinely mid-turn, regardless of what the trailing visible text looks like. This is
      // a DIFFERENT concern from questionsAnswered above (that one's "don't repeat a reply already
      // given for this question"; this one's "don't interrupt a turn in progress") — both are
      // needed, neither subsumes the other. The ⏺ response-marker / spinner-frame approach tried
      // first for this was abandoned — confirmed live (both directly and by a second independent
      // check) that character is ambiguous between a real response marker and a spinner-animation
      // frame, not a reliable turn signal.
      if (captureTerminal().includes('esc to interrupt')) return 'busy: esc-to-interrupt present';

      const current = stripAnsiForMatching(captureTerminal());
      if (!current.trim()) return 'empty pane';
      // Only treat it as "waiting on the human" once the pane has genuinely settled (not
      // mid-stream) — combined with "esc to interrupt" already confirmed absent above, this alone
      // is sufficient to mean it's genuinely the human's turn: Claude Code doesn't sit idle at its
      // own prompt for any other reason.
      await waitForPaneStable(1_500);
      if (captureTerminal().includes('esc to interrupt')) return 'busy: esc-to-interrupt appeared during settle wait';
      const settled = stripAnsiForMatching(captureTerminal());
      if (settled !== current) return 'not settled: pane still changing';

      // No trailing-"?" requirement here any more — deliberately removed, not just relaxed.
      // Confirmed live it caused a real stall: Claude Code's own "confirm this or correct it"
      // prompts (a numbered list of proposed defaults, ending in "tell me 'go' or correct any:")
      // don't reliably end in a literal "?" — the real "?" can be buried mid-message, well before
      // the true tail, and the phrasing is still genuinely the human's turn even though the last
      // sentence is a period. Since "esc to interrupt" absent + pane settled already only fires
      // once a turn has genuinely ended, an additional content-based gate here was redundant at
      // best and actively wrong for this common phrasing at worst.
      const tail = settled.slice(-3_000);
      if (questionsAnswered.has(tail)) return 'already answered this exact question, skipping';

      // A real clarifying-question exchange: stop marking dead air, put a beat on screen, and type
      // the answer out at real speed so the plain-language back-and-forth is actually watchable.
      // Only the model call that generates the answer is bracketed as its own short wait.
      await leaveWait();
      if (questionBeatsShown === 0) {
        await beat(
          page,
          'note',
          'The agent has a question for the designer. The answer goes back the same way the brief ' +
            'came in: plain language, nothing technical. This conversation is the design work.',
          { position: 'top' }
        );
      } else {
        await beat(
          page,
          'note',
          "Another question, answered the same way: in the designer's own terms.",
          { position: 'top' }
        );
      }
      questionBeatsShown++;
      await enterWait('act2-answer', ANSWER_CHIP);
      const answer = await generateDesignerAnswer(tail);
      await leaveWait();
      await waitForPaneStable();
      await sendTerminalText(answer);
      sendTerminalKey('Enter');
      questionsAnswered.add(tail);
      await page.waitForTimeout(500);
      await enterWait('act2-design', DESIGN_CHIP);
      return `generated answer sent: "${answer}"`;
    }

    // No proactive token-refresh cycle here any more (see the removed refreshStoredMcpToken/
    // reconnectMcpAndNudge's own remarks above) — just a steady poll for whether the agent is
    // genuinely waiting on a clarifying-question answer.
    //
    // Logs every tick's outcome, not just errors — confirmed live this is load-bearing, not just
    // verbose: a real take stalled ~9 minutes with the agent genuinely idle and nothing ever sent,
    // and the run log showed nothing either way, making it impossible after the fact to tell
    // "the mechanism correctly decided there was nothing to do" apart from "the mechanism silently
    // died." try/catch alone doesn't fully cover this either — `keepalive` is a floating promise
    // nobody calls .catch() on directly, so Node only surfaces an unhandled rejection once
    // something eventually awaits it (the finally block below), which can be arbitrarily later
    // than the real failure, or never if the process is killed first. Catching and logging inline,
    // per tick, means a thrown error shows up in the run log at the moment it actually happens,
    // and — just as important — the loop keeps going afterward instead of dying permanently on one
    // bad iteration.
    let stopKeepalive = false;
    let keepaliveTick = 0;
    const keepalive = (async () => {
      while (!stopKeepalive) {
        await page.waitForTimeout(5_000);
        keepaliveTick++;
        // If an exception broke the answer/design wait handoff mid-exchange, re-arm the silent
        // design wait so the rest of the stretch is still marked (and still speeds up) rather than
        // recording at full length.
        if (!openWaitLabel) await enterWait('act2-design', DESIGN_CHIP).catch(() => {});
        try {
          const outcome = await respondToLiveQuestionIfWaiting();
          console.log(`[keepalive #${keepaliveTick}] ${outcome}`);
        } catch (err) {
          console.error(`[keepalive #${keepaliveTick}] caught error, continuing: ${err instanceof Error ? err.stack ?? err.message : String(err)}`);
        }
      }
    })();

    try {
      await expect.poll(
        async () => {
          const token = await mintAgentToken();
          if (!token) return false;

          // The brief never told the agent what definitionKey or exact displayName to use (see
          // its own remarks above) — discover whatever it actually chose instead of assuming a
          // fixed key. The seeded "reference-demo" blueprint is the only one known to exist at
          // the start of this act, so the first OTHER entry the list endpoint reports is the
          // agent's own new creation, whatever it named it.
          if (!newDefinitionKey) {
            const listResp = await request.get('/umbraco/management/api/v1/wayfinder/service-blueprints', {
              headers: { Authorization: `Bearer ${token}` }, ignoreHTTPSErrors: true
            }).catch(() => null);
            if (!listResp?.ok()) return false;
            const summaries: Array<{ definitionKey: string; displayName: string }> = await listResp.json();
            const created = summaries.find(s => s.definitionKey !== seededDefinitionKey);
            if (!created) return false;
            newDefinitionKey = created.definitionKey;
            newDisplayName = created.displayName;
          }

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
        { timeout: 55 * 60_000, intervals: [10_000] }
      ).toBe(true);
    } finally {
      stopKeepalive = true;
      await keepalive;
      await leaveWait();
    }

    await beat(
      page,
      'recap',
      'The blueprint is saved to the live engine. Working from the GDS Service Standard and ' +
        "Wayfinder's own guidance, the agent turned the brief into a sequence of stages, an " +
        'eligibility decision, a document upload, and a caseworker review.',
      { position: 'top' }
    );
  });

  test('Act 3 — publishing it to the site', async () => {
    await beat(page, 'intent', 'The blueprint is content in Umbraco now. Let us put it on the site.');

    await page.goto('/umbraco/section/content');
    await page.waitForTimeout(1_500);
    await humanClick(page, page.getByText('Apply', { exact: true }).first());
    await page.waitForTimeout(1_500);

    await beat(page, 'setup', 'This block renders the Apply page. Today it points at a placeholder service.');
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
      'One field, one publish. Apply now serves the service the designer described.'
    );
  });

  test('Act 4 — the blueprint in the visual editor', async () => {
    await beat(page, 'intent', 'Same blueprint, opened in the visual editor a service designer would use. Let us see what the words became.');

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

    await beat(page, 'setup', 'Every stage, every decision, and every route the agent wrote.');
    await humanClick(page, page.getByRole('button', { name: 'Fit to screen' }));
    await page.waitForTimeout(600);

    await beat(
      page,
      'recap',
      'The eligibility decision, the document upload, and the review and declaration step: each ' +
        'one traces back to a line in the brief.'
    );

    // Translate Wayfinder's implementation back to the domain requirement it satisfies — never
    // the other way round. Deliberately generic rather than matching specific stage names/wording:
    // the brief never told the agent what to call anything, so this finds whichever stage the
    // agent actually made into the branch point (2+ outgoing routes = a real decision, not just a
    // straight-through hop) rather than assuming a name. Best-effort — if the agent's own design
    // shape doesn't match this expectation for some reason, skip gracefully rather than fail the
    // whole act over a narration flourish.
    const canvas = page.getByRole('application', { name: /graph canvas/i });
    const stageNodes = canvas.getByRole('button', { name: /Applicant queue|Caseworker queue/ });
    const stageCount = await stageNodes.count();
    let shownBranchPoint = false;
    for (let i = 0; i < stageCount && !shownBranchPoint; i++) {
      await humanClick(page, stageNodes.nth(i));
      await page.waitForTimeout(500);
      const routes = page.getByRole('region', { name: 'Outgoing routes' }).getByRole('article');
      if ((await routes.count()) >= 2) {
        shownBranchPoint = true;
        await beat(
          page,
          'note',
          'The designer said only jugglers from a recognised authority can transfer. Here that ' +
            'rule is a decision point: each route out of this step carries its own condition, ' +
            'checked before the applicant goes any further.',
          { position: 'top' }
        );
        await page.waitForTimeout(1_000);
      }
    }

    await beat(
      page,
      'note',
      'Further round the graph, the document upload holds the evidence they asked to see, and the ' +
        'caseworker review holds the decision they said a person must always make.'
    );

    await humanClick(page, page.getByRole('tab', { name: /validation/i }));
    await page.waitForTimeout(800);
    await beat(
      page,
      'note',
      'The blueprint is valid and complete. This is the picture a team would sketch on a wall to ' +
        'agree how a service works. Here it is running.'
    );
  });

  test('Act 5 — running the service', async () => {
    // The default 5-minute config timeout isn't enough for a multi-step generic walk (up to 8
    // stages, each with real human-paced typing/clicks) plus the caseworker half of the act —
    // confirmed live, the default budget ran out mid-walk.
    test.setTimeout(10 * 60_000);

    await beat(page, 'setup', 'Now as an applicant.');
    await page.goto('/demo/login');
    await humanClick(page, page.getByRole('button', { name: /Alex Applicant/i }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    await beat(page, 'intent', 'We will walk the journey the agent designed, the way a member of the public would.');
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
        await beat(page, 'note', 'The document upload the designer asked for, working.');
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

      // Wayfinder's "radio" component is a real GOV.UK radio group — never handled by this walk
      // at all until now. Confirmed live this is load-bearing, not cosmetic: a real design's
      // eligibility question ("Which authority issued your current licence?") rendered as a radio
      // group as its very first stage, so leaving it unanswered meant every later stage was
      // unreachable and the request never reached the caseworker queue — the walk quietly
      // exhausted its whole step budget stuck on stage one. Group by `name` (GOV.UK radios in one
      // question always share it) and pick one option per group — preferring an option whose own
      // label doesn't read as a negative/opt-out choice ("none of these", "none of the above",
      // "not sure"), so the happy path stays eligible rather than randomly bailing out into a
      // rejection branch.
      const radios = main.locator('input[type="radio"]');
      const radioCount = await radios.count();
      if (radioCount > 0) {
        const radioGroupNames = new Set<string>();
        for (let i = 0; i < radioCount; i++) {
          const name = await radios.nth(i).getAttribute('name');
          if (name) radioGroupNames.add(name);
        }
        for (const name of radioGroupNames) {
          const group = main.locator(`input[type="radio"][name="${name}"]`);
          if (await group.locator(':checked').count() > 0) continue;
          const optionCount = await group.count();
          let chosen = group.first();
          for (let i = 0; i < optionCount; i++) {
            const option = group.nth(i);
            const id = await option.getAttribute('id');
            const labelText = id ? await page.locator(`label[for="${id}"]`).innerText().catch(() => '') : '';
            if (!/none of these|none of the above|not sure|don'?t know/i.test(labelText)) {
              chosen = option;
              break;
            }
          }
          await humanClick(page, chosen);
        }
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
      //
      // Short value for text/textarea, not a long sentence — confirmed live this is also load-
      // bearing: a real design's "licence number" text field declared maxLength: 20 server-side
      // (GovUkFields.cs's RenderText never emits a client-visible HTML maxlength attribute, so
      // there's no signal in the DOM to size against), and the old 48-character fixed sentence
      // failed that validation identically every time, silently re-displaying the same stage until
      // the step budget ran out with the request never reaching the caseworker queue — the exact
      // same failure shape as the file-upload/radio/date fixes above, just one more real constraint
      // an agent-authored form can impose that this generic walk needed to be robust to.
      const typedFills: Array<[string, string]> = [
        ['input[type="text"]', 'JL-123456'],
        ['input:not([type])', 'JL-123456'],
        ['textarea', 'JL-123456'],
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

    await beat(page, 'recap', 'Submitted: eligibility, evidence, review, and declaration, in the order the designer set out.');

    await beat(page, 'setup', 'And now as the caseworker who picks it up.');
    await humanClick(page, page.getByRole('button', { name: 'Sign out', exact: true }).or(page.locator('button', { hasText: 'Sign out' })).first());
    await page.waitForTimeout(500);
    await page.goto('/demo/login');
    await humanClick(page, page.getByRole('button', { name: /Casey Caseworker/i }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    await beat(page, 'intent', "This is the caseworker queue. The blueprint's own routing sent the request straight here.");
    await humanClick(page, page.getByRole('link', { name: 'Caseworker queue', exact: true }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    await expect(page.getByText(newDisplayName).first()).toBeVisible({ timeout: 15_000 });

    await beat(
      page,
      'recap',
      'One brief, one conversation, a working service: described in plain language, published in ' +
        'Umbraco, and run by real people.'
    );
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
