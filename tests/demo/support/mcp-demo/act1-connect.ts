import type { Page } from '@playwright/test';
import { existsSync, readFileSync } from 'node:fs';
import {
  beat,
  captureTerminal,
  humanClick,
  humanType,
  moveNarrationTo,
  sendTerminalKey,
  sendTerminalText,
  showTerminalMirror,
  startDemoTerminalSession,
  stripAnsiForMatching,
  waitForPaneStable
} from 'wayfinder-demo-recording-kit';
import { approveAuthorizationInBackoffice } from './backoffice-authorization.js';
import { adminCredentials, claudeSessionLogPath, mcpUrl, scratchDir } from './demo-config.js';

// Read the authorize URL from the full session log (script -F), never the wrapped pane: the
// query string is ~400 chars and the pane is 150 cols, so a pane scrape truncates it at the
// first wrap. `g` flag so we can take the most recent match after each attempt.
const authorizeUrlPattern = /https:\/\/localhost:44399\/umbraco\/management\/api\/v1\/security\/back-office\/authorize\?\S+/g;

const MAX_CONNECT_ATTEMPTS = 3;

const readSessionLog = (): string => (existsSync(claudeSessionLogPath) ? readFileSync(claudeSessionLogPath, 'utf8') : '');

async function signInToBackoffice(page: Page): Promise<void> {
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
}

async function openTerminal(page: Page): Promise<void> {
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
  // system/keychain trust store `dotnet dev-certs https --trust` populates — confirmed live,
  // `claude mcp` calls fail UNABLE_TO_VERIFY_LEAF_SIGNATURE against this self-signed dev cert
  // until this is set. BROWSER=true (the /usr/bin/true no-op) belt-and-braces stops any browser
  // spawn; the OAuth step below uses `claude mcp login --no-browser`, which prints the authorize
  // URL rather than opening one, and we drive that URL in the recorded page. Both scoped to this
  // throwaway localhost session. waitForPaneStable() before EVERY send, not just the first — a
  // send-keys call made before bash has redrawn its prompt silently loses leading characters.
  await waitForPaneStable();
  await sendTerminalText('export NODE_TLS_REJECT_UNAUTHORIZED=0');
  sendTerminalKey('Enter');
  await waitForPaneStable();
  await sendTerminalText('export BROWSER=true');
  sendTerminalKey('Enter');
  await waitForPaneStable();
}

/** Registers the MCP server, starts `claude mcp login`, and waits for the authorize URL it prints (or '' if none appears). */
async function startMcpLogin(page: Page): Promise<{ logOffset: number; authUrl: string }> {
  // `remove` joined with `;` not `&&`: it exits 1 when there's nothing to remove (the normal
  // case on a fresh scratch dir), which `&&` would let short-circuit the whole line.
  await sendTerminalText(
    'claude mcp remove wayfinder-umbraco 2>/dev/null; ' +
      `claude mcp add --transport http wayfinder-umbraco ${mcpUrl} ` +
      '--client-id umbraco-back-office-wayfinder-mcp --callback-port 33418'
  );
  sendTerminalKey('Enter');
  await waitForPaneStable();

  // `claude mcp add` only registers the server (Claude Code 2.1.x). `claude mcp login
  // --no-browser` runs the OAuth 2.1 + PKCE handshake: it prints the authorize URL, then
  // blocks on stdin waiting for the redirect URL to be pasted back.
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
  return { logOffset, authUrl };
}

/** Approves the authorization in the recorded backoffice and pastes the redirect back to the CLI. */
async function completeMcpLogin(page: Page, authUrl: string, logOffset: number, attempt: number): Promise<void> {
  await beat(
    page,
    'note',
    'That is the standard backoffice sign-in. Approve it once, and the design partner has a ' +
      'session that refreshes itself for the rest of the work.',
    { position: 'top' }
  );
  const redirectUrl = await approveAuthorizationInBackoffice(page, authUrl);

  await showTerminalMirror(page);
  await page.waitForTimeout(500);
  await waitForPaneStable();
  if (!redirectUrl) {
    console.log(`MCP connection attempt ${attempt}: no loopback callback captured from the authorize redirect.`);
    sendTerminalKey('C-c');
    await page.waitForTimeout(1_000);
    return;
  }

  await sendTerminalText(redirectUrl);
  sendTerminalKey('Enter');
  // Wait for `claude mcp login` to consume the pasted URL and finish the token exchange,
  // then clear the pane: the tty tends to echo a copy of the long URL onto the bash line
  // once login exits, and the URL's own `&` chars are shell job-control if bash runs it.
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
}

/** Runs `claude mcp list` and reports whether the server shows as Connected. */
async function mcpListShowsConnected(page: Page, attempt: number): Promise<boolean> {
  await waitForPaneStable();
  await sendTerminalText('claude mcp list');
  sendTerminalKey('Enter');
  // `claude mcp list` runs its own live health check ("Checking MCP server health…") — poll
  // for the real outcome text rather than trusting waitForPaneStable alone.
  const listDeadline = Date.now() + 20_000;
  let listOutcome = '';
  while (Date.now() < listDeadline) {
    listOutcome = stripAnsiForMatching(captureTerminal());
    if (/wayfinder-umbraco.*(Connected|Failed to connect|[Nn]eeds authentication)/i.test(listOutcome)) break;
    await page.waitForTimeout(500);
  }
  const connected = /wayfinder-umbraco.*Connected/i.test(listOutcome);
  if (!connected) {
    console.log(`MCP connection attempt ${attempt} did not report Connected: ${listOutcome.slice(-300)}`);
  }
  return connected;
}

async function attemptMcpConnection(page: Page, attempt: number): Promise<boolean> {
  const { logOffset, authUrl } = await startMcpLogin(page);
  if (authUrl) {
    await completeMcpLogin(page, authUrl, logOffset, attempt);
  } else {
    console.log(`MCP connection attempt ${attempt}: no authorization URL appeared in the session log.`);
    sendTerminalKey('C-c');
  }
  return mcpListShowsConnected(page, attempt);
}

/**
 * Hard verification gate, not a fixed wait: confirmed live in an earlier version of this Act that
 * this is load-bearing — a run once proceeded straight to launching the real (expensive, 30-40
 * minute) recorded agent even though `claude mcp list` had just printed a failure, because nothing
 * checked its output. Retry the whole add/login/list sequence, and fail the test outright — never
 * launch the agent — if it still isn't connected after real retries.
 */
async function connectMcpServer(page: Page): Promise<void> {
  for (let attempt = 1; attempt <= MAX_CONNECT_ATTEMPTS; attempt++) {
    if (await attemptMcpConnection(page, attempt)) {
      return;
    }
  }
  throw new Error(
    `wayfinder-umbraco MCP server never reported Connected after ${MAX_CONNECT_ATTEMPTS} attempts — refusing to launch the recorded agent with no tools available.`
  );
}

/** Act 1: sign in to the backoffice, then connect the AI design partner to its MCP authoring tools. */
export async function connectDesignPartner(page: Page): Promise<void> {
  await signInToBackoffice(page);
  await openTerminal(page);
  await connectMcpServer(page);

  await beat(
    page,
    'recap',
    'The design partner is connected. From here it works like any colleague with a login: it can ' +
      'read and author service blueprints through the same tools a person would use.',
    { position: 'top' }
  );
}
