import type { APIRequestContext, Page } from '@playwright/test';
import {
  beat,
  clearSlate,
  sendTerminalKey,
  sendTerminalText,
  showSlate,
  waitForPaneStable,
  waitForPromptText,
  waitForPromptTextGone
} from 'wayfinder-demo-recording-kit';
import { AgentConversation } from './agent-conversation.js';
import { DESIGNER_BRIEF } from './designer-brief.js';
import { saveRehearsalBlueprint, waitForAgentDesign } from './saved-blueprint.js';

/** The Act 2 completion-poll budget (see test.setTimeout in the spec): a genuinely good conversation that pauses to ask real clarifying questions can legitimately run well past 35 minutes. */
export const ACT2_TIMEOUT_MS = 65 * 60_000;

/**
 * Handles the one-time consent gates a freshly launched `claude` can show on a fresh scratch
 * directory, confirmed live: both showed up and neither is the other.
 * (1) the workspace-trust gate ("Is this a project you created or trust?"), which fires first and
 * is NOT caught by only checking for the second gate's own text — the brief's text then gets typed
 * straight into that still-open menu and corrupts the underlying shell.
 * (2) the BypassPermissions gate ("Yes, I accept"). Neither appears on every Claude Code version,
 * and neither should be answered blindly.
 *
 * Checking the LIVE pane (waitForPromptText) rather than a rolling session-log tail matters, not
 * just tidier: a rolling buffer can still contain a gate's option text well after that gate was
 * dismissed. Both gates' option text includes "No, exit", so a log-tail match on that phrase
 * couldn't tell "gate 1 still showing" from "gate 1 handled, now stale scrollback", which once
 * caused a stray "2" to be sent into the live, ready prompt. Matching on "Yes, I accept" (unique to
 * gate 2; gate 1's own affirmative reads "Yes, I trust this folder") removes the ambiguity, and
 * waiting for each gate's text to actually disappear confirms it was really dismissed.
 *
 * Gate 1 is a plain arrow-selectable list on this Claude Code version (2.1.283), not a numbered
 * menu, "No, exit" highlighted first, "Yes, I trust this folder" second: sending the digit "1" does
 * nothing, which silently corrupted a take that assumed an older numbered menu. Down then Enter is
 * what moves the selection and confirms it. Gate 2 still responds to a plain digit.
 */
async function acceptConsentGates(page: Page): Promise<void> {
  if (await waitForPromptText(/trust this folder/i, 8_000)) {
    await waitForPaneStable();
    sendTerminalKey('Down');
    await page.waitForTimeout(300);
    sendTerminalKey('Enter');
    await waitForPromptTextGone(/trust this folder/i, 5_000);
  }

  if (await waitForPromptText(/Yes, I accept/i, 5_000)) {
    await waitForPaneStable();
    await sendTerminalText('2');
    sendTerminalKey('Enter');
    await waitForPromptTextGone(/Yes, I accept/i, 5_000);
  }
}

/**
 * --tools restricts the *entire* available toolset (not an allow-list layered on the default one)
 * to just this MCP server's tools plus the built-in MCP-resource readers — without it Claude Code's
 * own Agent/Task tool stays available and has been observed to spontaneously delegate a call to a
 * background sub-agent fork that never returns. --permission-mode bypassPermissions is safe
 * specifically because --tools has already narrowed the whole session to those calls against this
 * local dev stack. --model sonnet pins the model so the agent doesn't inherit an unrelated personal
 * default. Haiku was tried as a cost lever but confirmed live not to work in this restricted-tools
 * sandbox: instead of calling its MCP tools it hallucinated raw <function_calls><invoke
 * name="bash"> text and tried non-existent commands, then gave up and asked unanswerable
 * clarifying questions. Sonnet completed this exact task correctly on the first two real attempts.
 */
async function launchAgent(page: Page): Promise<void> {
  await waitForPaneStable();
  await sendTerminalText(
    'claude --model sonnet ' +
      '--tools "mcp__wayfinder-umbraco__*,ListMcpResourcesTool,ReadMcpResourceDirTool,ReadMcpResourceTool" ' +
      '--permission-mode bypassPermissions'
  );
  sendTerminalKey('Enter');
  await acceptConsentGates(page);
}

/**
 * The brief is long enough that typing it into the bounded tmux pane scrolls earlier lines away
 * before a viewer ever sees the whole thing at once — a terminal-viewport problem, not an
 * animation-speed one, so a full-screen slate (immune to pane scrolling) shows the complete text
 * first, held for a genuinely reading-paced duration, before it's sent to the terminal at all.
 * bodyStyle overrides the slate's default centered/no-wrap styling with left-aligned pre-wrap so the
 * brief's own paragraph breaks and bullet list render as written.
 */
async function presentAndSendBrief(page: Page): Promise<void> {
  await showSlate(page, {
    eyebrow: 'THE BRIEF',
    title: "The brief, in the designer's words",
    body: DESIGNER_BRIEF,
    bodyStyle: { whiteSpace: 'pre-wrap', textAlign: 'left', maxWidth: '980px', fontSize: '22px' }
  });
  await clearSlate(page);

  await waitForPaneStable();
  await sendTerminalText(DESIGNER_BRIEF, 12);
  await page.waitForTimeout(300);
  sendTerminalKey('Enter');
}

/** Act 2: the designer's brief goes to the agent, which designs and saves the blueprint (answering its questions as the designer along the way). */
export async function runBrief(page: Page, request: APIRequestContext): Promise<void> {
  if (process.env.DEMO_REHEARSAL === '1') {
    await beat(page, 'note', "[Rehearsal mode] Faking the agent's end state instead of a real call.");
    await saveRehearsalBlueprint(request, ms => page.waitForTimeout(ms));
    return;
  }

  await beat(
    page,
    'setup',
    "This is the design partner. Everything it does from here goes through Wayfinder's " + 'service-blueprint authoring tools.',
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

  await launchAgent(page);
  await presentAndSendBrief(page);

  const conversation = new AgentConversation(page);
  await conversation.start();
  try {
    await waitForAgentDesign(request);
  } finally {
    await conversation.stop();
  }
  await conversation.waitForTurnToEnd();

  await beat(
    page,
    'recap',
    'The blueprint is saved to the live engine. Working from the GDS Service Standard and ' +
      "Wayfinder's own guidance, the agent turned the brief into a sequence of stages, an " +
      'eligibility decision, a document upload, and a caseworker review.',
    { position: 'top' }
  );
}
