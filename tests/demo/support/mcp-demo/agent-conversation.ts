import type { Page } from '@playwright/test';
import { existsSync, readFileSync } from 'node:fs';
import {
  beat,
  captureTerminal,
  hideFastForwardChip,
  markWaitEnd,
  markWaitStart,
  sendTerminalKey,
  sendTerminalText,
  showFastForwardChip,
  showTerminalMirror,
  stripAnsiForMatching,
  waitForPaneStable
} from 'wayfinder-demo-recording-kit';
import { approveAuthorizationInBackoffice } from './backoffice-authorization.js';
import { claudeSessionLogPath } from './demo-config.js';
import { generateDesignerAnswer } from './designer-brief.js';

// Dead-air handling for this act. The agent's silent design stretches are the only genuine
// dead air — nothing for a viewer to read, real wall-clock time on a live external process
// that can't be sped up live. Those are marked as wait segments (compressed afterward, with a
// visible "fast-forwarding" chip so the sped-up frames read as "moving through waiting time").
// The clarifying-question exchanges are deliberately NOT marked: the designer's answer is
// typed out at real speed, because that plain-language back-and-forth is the design work and
// the part worth watching.
const DESIGN_CHIP = 'The agent is designing the blueprint';
const ANSWER_CHIP = 'Waiting for the designer';
const DESIGN_WAIT = 'act2-design';
const ANSWER_WAIT = 'act2-answer';

/** Claude Code's own TUI state indicator: present ONLY while it's actively working, removed the instant it's back at an idle prompt. */
const isBusy = (): boolean => captureTerminal().includes('esc to interrupt');

// Confirmed live (a separate, smaller recording in this same toolkit turned this up): claude mcp
// login (Act 1) authenticates the CLI's own meta-commands (mcp add/list), but a freshly launched
// interactive agent session still hits its own, separate "1 MCP server needs authentication" gate
// on its first real tool call, printing a fresh authorize URL into the conversation and waiting for
// a human to drive it. The URL must come from the session log's own OSC 8 hyperlink target, not the
// visually wrapped text the TUI renders: this URL is long enough to hard-wrap as real line breaks
// at 150 columns, truncating a plain \S+ match at the first wrap, while the OSC 8 URI parameter
// carries the whole thing with no wrapping.
const midConversationAuthorizeUrlPattern =
  /\x1b\]8;[^;]*;(https:\/\/localhost:44399\/umbraco\/management\/api\/v1\/security\/back-office\/authorize\?[^\x07\x1b]+)/g;

/**
 * The live conversation with the design agent while it works on the brief. Owns what has to be
 * tracked across its turns — which dead-air stretch is open, which questions were already answered,
 * whether the agent has started — and the keepalive loop that notices when the agent is waiting on
 * the human (a clarifying question, or an authorization prompt) and answers it.
 */
export class AgentConversation {
  private readonly _briefSentAt = Date.now();
  private _agentHasBeenBusy = false;
  private _openWaitLabel: string | null = null;
  private _lastHandledAuthUrl = '';
  private readonly _questionsAnswered = new Set<string>();
  private _questionBeatsShown = 0;
  private _stopKeepalive = false;
  private _keepaliveTick = 0;
  private _keepalive: Promise<void> | null = null;

  constructor(private readonly _page: Page) {}

  /** Opens the first dead-air stretch and starts the keepalive loop. */
  async start(): Promise<void> {
    await this._enterWait(DESIGN_WAIT, DESIGN_CHIP);
    this._keepalive = this._keepaliveLoop();
  }

  async stop(): Promise<void> {
    this._stopKeepalive = true;
    await this._keepalive;
  }

  /**
   * The save has landed in the engine, but the agent is usually still finishing its wrap-up message
   * in the terminal. Let its turn actually end — "esc to interrupt" gone and the pane settled —
   * before cutting to the backoffice, so the recording never leaves it visibly mid-sentence. Still
   * inside the design wait, so this stretch stays compressed.
   */
  async waitForTurnToEnd(): Promise<void> {
    const deadline = Date.now() + 120_000;
    while (Date.now() < deadline) {
      if (!isBusy()) {
        await waitForPaneStable(3_000);
        if (!isBusy()) break;
      }
      await this._page.waitForTimeout(3_000);
    }
    await this._page.waitForTimeout(2_000);
    await this._leaveWait();
  }

  // ── dead-air markers ──────────────────────────────────────────────────────

  /** enterWait/leaveWait keep the marker, the chip, and this local mirror in lockstep so an exception mid-exchange can't leave a stretch unmarked. */
  private async _enterWait(label: string, chipText: string): Promise<void> {
    if (this._openWaitLabel) return;
    markWaitStart(label);
    this._openWaitLabel = label;
    await showFastForwardChip(this._page, chipText);
  }

  private async _leaveWait(): Promise<void> {
    if (!this._openWaitLabel) return;
    markWaitEnd();
    this._openWaitLabel = null;
    await hideFastForwardChip(this._page);
  }

  // ── keepalive ─────────────────────────────────────────────────────────────

  /**
   * A steady poll for whether the agent is genuinely waiting on the human. (A proactive token-refresh
   * + /mcp-Reconnect-and-nudge mechanism used to live alongside it and was removed — confirmed live
   * it was net-harmful: it sent "Reconnected. Please retry." unconditionally, even when the /mcp
   * reconnect itself had failed, sending the agent retrying into a connection with zero MCP tools. A
   * genuine token expiry is already handled fine without it: the agent's own tool calls fail with a
   * real auth error and it correctly stops and asks.)
   *
   * Logs every tick's outcome, not just errors — confirmed live this is load-bearing: a real take
   * stalled ~9 minutes with the agent genuinely idle and nothing ever sent, and the run log showed
   * nothing either way, making it impossible after the fact to tell "the mechanism correctly decided
   * there was nothing to do" apart from "the mechanism silently died." try/catch alone doesn't fully
   * cover this either — the loop is a floating promise nobody calls .catch() on directly, so Node
   * only surfaces an unhandled rejection once something eventually awaits it, which can be
   * arbitrarily later than the real failure. Catching and logging inline, per tick, means a thrown
   * error shows up in the run log at the moment it happens and the loop keeps going afterward.
   */
  private async _keepaliveLoop(): Promise<void> {
    while (!this._stopKeepalive) {
      await this._page.waitForTimeout(5_000);
      this._keepaliveTick++;
      if (isBusy()) this._agentHasBeenBusy = true;
      // If an exception broke the answer/design wait handoff mid-exchange, re-arm the silent design
      // wait so the rest of the stretch is still marked (and still speeds up) rather than recording
      // at full length.
      if (!this._openWaitLabel) await this._enterWait(DESIGN_WAIT, DESIGN_CHIP).catch(() => {});
      try {
        await this._driveAuthorizationIfWaiting();
        console.log(`[keepalive #${this._keepaliveTick}] ${await this._respondToQuestionIfWaiting()}`);
      } catch (err) {
        console.error(`[keepalive #${this._keepaliveTick}] caught error, continuing: ${err instanceof Error ? (err.stack ?? err.message) : String(err)}`);
      }
    }
  }

  // ── the agent asks to be authorized again ─────────────────────────────────

  /**
   * An authorization code can expire before the agent finishes processing it, and the agent then
   * asks for a fresh URL itself rather than getting stuck (confirmed live), so this re-triggers on
   * any new, different authorize URL rather than only ever handling the first one it sees.
   */
  private async _driveAuthorizationIfWaiting(): Promise<void> {
    const logText = existsSync(claudeSessionLogPath) ? readFileSync(claudeSessionLogPath, 'utf8') : '';
    const matches = [...logText.matchAll(midConversationAuthorizeUrlPattern)].map(m => m[1]);
    if (!matches.length) return;
    const authUrl = matches[matches.length - 1];
    if (authUrl === this._lastHandledAuthUrl) return;
    this._lastHandledAuthUrl = authUrl;

    const redirectUrl = await approveAuthorizationInBackoffice(this._page, authUrl);
    await this._showTerminalAfterRedirect();
    if (!redirectUrl) {
      console.log('Mid-conversation authorization: no loopback callback captured from the authorize redirect.');
      return;
    }
    await waitForPaneStable();
    await sendTerminalText(redirectUrl);
    sendTerminalKey('Enter');
    await this._page.waitForTimeout(1_500);
  }

  /**
   * The OAuth redirect chain (login -> consent -> loopback callback) can still be settling its own
   * navigation right here, confirmed live: showTerminalMirror's own page.evaluate raced an in-flight
   * navigation and threw "Execution context was destroyed". A settle wait plus one retry covers it
   * without guessing a fixed delay.
   */
  private async _showTerminalAfterRedirect(): Promise<void> {
    await this._page.waitForLoadState('load', { timeout: 5_000 }).catch(() => {});
    await this._page.waitForTimeout(500);
    try {
      await showTerminalMirror(this._page);
    } catch {
      await this._page.waitForTimeout(1_000);
      await showTerminalMirror(this._page);
    }
    await this._page.waitForTimeout(500);
  }

  // ── the agent asks the designer a question ────────────────────────────────

  /**
   * Whether the agent has stopped and is waiting on the human, without sending anything. Returns a
   * reason when it is NOT the designer's turn, or the settled pane text when it is.
   *
   * "esc to interrupt" is checked FIRST, before anything content-based: it's a genuine UI state
   * signal, not a text-content heuristic, so this can never send while Claude is still genuinely
   * mid-turn, whatever the trailing visible text looks like. (The ⏺ response-marker / spinner-frame
   * approach tried first was abandoned — that character is ambiguous between a real response marker
   * and a spinner-animation frame.) Right after the brief is sent the pane sits idle only because
   * the agent hasn't started yet, not because it's asking anything — treating that as "the
   * designer's turn" injects a spurious opening exchange (seen live), so wait until the agent has
   * been busy once, or ~2 min have passed.
   */
  private async _paneAwaitingAnswer(): Promise<{ skip: string } | { tail: string }> {
    if (isBusy()) {
      this._agentHasBeenBusy = true;
      return { skip: 'busy: esc-to-interrupt present' };
    }

    const current = stripAnsiForMatching(captureTerminal());
    if (!current.trim()) return { skip: 'empty pane' };
    if (!this._agentHasBeenBusy && Date.now() - this._briefSentAt < 120_000) {
      return { skip: 'agent has not started working on the brief yet' };
    }

    // Only treat it as "waiting on the human" once the pane has genuinely settled (not mid-stream) —
    // combined with "esc to interrupt" already confirmed absent above, this alone is sufficient:
    // Claude Code doesn't sit idle at its own prompt for any other reason.
    await waitForPaneStable(1_500);
    if (isBusy()) {
      this._agentHasBeenBusy = true;
      return { skip: 'busy: esc-to-interrupt appeared during settle wait' };
    }
    const settled = stripAnsiForMatching(captureTerminal());
    if (settled !== current) return { skip: 'not settled: pane still changing' };

    // No trailing-"?" requirement — deliberately removed, not just relaxed. Confirmed live it caused
    // a real stall: Claude Code's own "confirm this or correct it" prompts (a numbered list of
    // proposed defaults, ending in "tell me 'go' or correct any:") don't reliably end in a literal
    // "?", yet are still genuinely the human's turn.
    return { tail: settled.slice(-3_000) };
  }

  /**
   * Returns a short description of what happened this tick, for the keepalive loop to log — every
   * tick, not just errors, so a future stall leaves a real trail instead of silence.
   */
  private async _respondToQuestionIfWaiting(): Promise<string> {
    const pane = await this._paneAwaitingAnswer();
    if ('skip' in pane) return pane.skip;
    // Dedup is separate from the busy check: that one's "don't interrupt a turn in progress", this
    // one's "don't repeat a reply already given for this exact question (e.g. a stale re-render)".
    if (this._questionsAnswered.has(pane.tail)) return 'already answered this exact question, skipping';

    // A real clarifying-question exchange: stop marking dead air, put a beat on screen, and type
    // the answer out at real speed so the plain-language back-and-forth is actually watchable.
    // Only the model call that generates the answer is bracketed as its own short wait.
    await this._leaveWait();
    await this._announceQuestion();
    await this._enterWait(ANSWER_WAIT, ANSWER_CHIP);
    const answer = await generateDesignerAnswer(pane.tail);
    await this._leaveWait();
    await waitForPaneStable();
    await sendTerminalText(answer);
    sendTerminalKey('Enter');
    this._questionsAnswered.add(pane.tail);
    await this._page.waitForTimeout(500);
    await this._enterWait(DESIGN_WAIT, DESIGN_CHIP);
    return `generated answer sent: "${answer}"`;
  }

  private async _announceQuestion(): Promise<void> {
    const first = this._questionBeatsShown === 0;
    await beat(
      this._page,
      'note',
      first
        ? 'The agent has a question for the designer. The answer goes back the same way the brief ' +
            'came in: plain language, nothing technical. This conversation is the design work.'
        : "Another question, answered the same way: in the designer's own terms.",
      { position: 'top' }
    );
    this._questionBeatsShown++;
  }
}
