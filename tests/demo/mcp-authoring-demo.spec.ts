import { test, expect, type Page } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
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
      //
      // remove, add, and list are sent as ONE atomic command line rather than three separate
      // sendTerminalText calls with a waitForPaneStable() between each — confirmed live this was a
      // real race, not resolved by tuning the stability window. waitForPaneStable() only proxies
      // "the screen looks quiet for ~450ms," not "the previous process has actually exited" — each
      // of these is a real subprocess with real (if usually brief) wall-clock work, and under the
      // heavier system load a real recording take runs under (Chromium + video encoding + the
      // dotnet app all concurrently), it's realistic for the pane to look quiet for 450ms while one
      // is still genuinely running and bash hasn't yet regained the terminal — so the next
      // command's keystrokes land on nothing and get silently dropped. Confirmed live, twice: a
      // real take where `claude mcp add` never appeared in the session log at all after `remove`
      // (not corrupted, completely absent), and separately a standalone reproduction where `claude
      // mcp list` vanished the same way after `add`. A lightweight standalone reproduction with no
      // browser/video overhead never hit either, consistent with a genuine timing race rather than
      // a logic bug.
      //
      // remove is joined with `;`, not `&&` — confirmed live this distinction is load-bearing:
      // `claude mcp remove` exits 1 (even with 2>/dev/null suppressing its error *message*) when
      // there's nothing to remove, which is the NORMAL case on every fresh scratch directory this
      // Act starts from — `&&` after it would silently short-circuit the whole chain before `add`
      // ever ran, on exactly the common case. add and list ARE joined with `&&`: add succeeding is
      // the expected case, and if it doesn't, there's nothing useful for list to check anyway.
      await sendTerminalText(
        'claude mcp remove wayfinder-umbraco 2>/dev/null; ' +
          'claude mcp add --transport http wayfinder-umbraco ' +
          'https://localhost:44399/wayfinder/service-blueprint-authoring/mcp ' +
          '--header "Authorization: Bearer $(jq -r .access_token /tmp/mcp-token.json)" && ' +
          'claude mcp list'
      );
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
      'This is the Claude CLI, connected through nothing but the MCP toolkit this host exposes — no ' +
        'special access, no shortcuts.',
      { position: 'top' }
    );
    await beat(
      page,
      'intent',
      'A real service designer is going to describe the problem in their own words — no Wayfinder ' +
        "terminology, just the juggling-licensing world they actually know — and we'll watch the " +
        'agent design the whole thing from that, asking questions of its own where it needs to.',
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

    // Pure domain language, no Wayfinder vocabulary anywhere — reviewed and approved verbatim by
    // the real user. This is the entire point of the demo: a service designer who has never heard
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
      "new licence from scratch — exactly the same as someone who's never juggled professionally ",
      "before. That's not fair on them, it duplicates assessment work that's already been done ",
      'properly elsewhere, and it puts off exactly the experienced jugglers we want performing here.\n\n',
      'I want a "transfer your licence" service instead. What I know about how it needs to work:\n',
      '- Only for jugglers who already hold a current licence from a juggling authority we formally ',
      'recognise — right now that\'s the European Juggling Federation, Async Circle International, ',
      "and the Ring Masters Guild. Anyone else isn't eligible for transfer; they need to apply as a ",
      'new licence holder instead, which is a separate existing service.\n',
      '- We need to see their current licence certificate and some proof of who they are.\n',
      '- Before we grant anything, they need to formally declare they\'ll uphold our professional ',
      'standards — same declaration a new applicant makes.\n',
      '- A caseworker always has to check the evidence and make the actual decision — this can\'t be ',
      'auto-approved, someone has to look at the documents.\n',
      '- Same accessibility bar as everything else we ship — WCAG double-A, in line with the GDS ',
      'service standard.\n\n',
      "Can you help me design this properly? Ask me anything you need."
    ].join('');

    // A small, prepared set of in-character domain answers — a real live agent's exact clarifying
    // questions are unpredictable, so this is matched loosely by topic keyword, the same way a
    // human operator playing this "designer" role would improvise. Answered only in the domain
    // language a licensing service designer would actually use — never Wayfinder terminology.
    const designerFaq: Array<{ topics: RegExp; answer: string }> = [
      {
        topics: /expir/i,
        answer: "If their recognised-authority licence has expired, they're not eligible for " +
          'transfer — they need to apply as a new licence holder instead, same as anyone without a ' +
          'recognised licence.'
      },
      {
        topics: /order|sequence|before.*document|document.*before|eligib.*(first|before|when)/i,
        answer: 'The eligibility check should come before we ask for any documents — no point ' +
          "asking for paperwork from someone who isn't eligible in the first place."
      },
      {
        topics: /identity|proof of who|only.*(licence|certificate)|missing.*document/i,
        answer: "If they can't provide proof of identity, only the licence certificate, then we " +
          "can't process the transfer — both documents are required before a caseworker can review it."
      },
      {
        topics: /reject|declin|turn(ed)? down/i,
        answer: 'If a caseworker rejects the application, the applicant should be told clearly why, ' +
          'and given the option to apply as a new licence holder instead if that\'s more appropriate.'
      },
      {
        topics: /queue|caseworker.*(team|group|multiple)|how many caseworker/i,
        answer: "Just one caseworker queue for now — this is a small transfer scheme."
      },
      {
        topics: /save.*(later|progress)|come back|partial|resume/i,
        answer: "Yes, being able to save and come back later would help — this isn't something " +
          'people can necessarily finish in one sitting, especially if they need to go find their ' +
          'documents.'
      }
    ];

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
    // it's done designing, and the "designer" persona should answer in domain language, live. A
    // live agent's exact wording is unpredictable, so this is a pragmatic best-effort check, not
    // an exhaustive one — the same way a human operator playing this role would improvise.
    // Tracks which QUESTIONS have already received a reply — keyed by the qualifying tail text
    // itself, regardless of whether the reply came from a designerFaq match or the generic
    // fallback. This went through two earlier, both-wrong keyings before landing here:
    // (1) keyed by pane-snapshot equality — too fragile, any small unrelated change elsewhere in
    //     the pane (a new tool-call line, a token count tick) made the snapshot "different" even
    //     though the same question was still the one being asked; the same answer went out
    //     identically five times in one real take.
    // (2) keyed by answer identity (a Set per FAQ entry/the fallback string) — better, but still
    //     wrong in a different way: designerFaq's regexes are coarse keyword matches (e.g. entry 0
    //     is /expir/i, meant for "has their licence expired"), and a LATER, genuinely different
    //     question can innocently contain the same keyword (confirmed live: "does another system
    //     issue the new certificate? Any new expiry date to show them?" re-matched entry 0 via the
    //     word "expiry" alone) — the answer-identity key then permanently blocked EVERY future
    //     tick, since the same stale entry kept "matching" and was already marked used.
    // Keying by the tail text itself sidesteps both failure modes: a genuinely persisting
    // unanswered question keeps producing the same (or a stable) tail and is correctly deduped,
    // while a genuinely new question — even one that happens to trigger the same FAQ regex via a
    // shared keyword — produces different tail text and gets a real, fresh reply regardless of
    // which FAQ entry (or the fallback) ends up answering it.
    const questionsAnswered = new Set<string>();
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

      const match = designerFaq.find(entry => entry.topics.test(tail));
      const answer = match
        ? match.answer
        : "Good question — use your best judgement on that one, based on how the rest of the " +
          'service works; I trust you to make a sensible call.';
      await waitForPaneStable();
      await sendTerminalText(answer);
      sendTerminalKey('Enter');
      questionsAnswered.add(tail);
      await page.waitForTimeout(500);
      return match
        ? `matched FAQ entry ${designerFaq.indexOf(match)} — sent`
        : 'fallback — sent';
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
          'You said only jugglers from a recognised authority can transfer — here, each outgoing ' +
            'route has its own "Available when" condition, which is exactly where Wayfinder ' +
            'implemented that eligibility rule as real routing logic, evaluated before the ' +
            "applicant even reaches the rest of the form.",
          { position: 'top' }
        );
        await page.waitForTimeout(1_000);
      }
    }

    await beat(
      page,
      'note',
      'And the document upload and the caseworker review, further round the graph, are the other ' +
        "two requirements from the brief — the evidence you asked to see, and the decision you " +
        'said always needs a person, not an automatic approval.'
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
