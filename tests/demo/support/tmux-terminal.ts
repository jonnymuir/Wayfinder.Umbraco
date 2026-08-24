import { execFileSync, spawn } from 'node:child_process';
import type { Page } from '@playwright/test';

// A recording-friendly terminal surface: the real session lives in tmux (on its own socket, so
// its environment and lifecycle are fully ours), input goes in via `tmux send-keys`, and the
// recorded page renders a styled mirror of `tmux capture-pane` output as plain DOM. Ported from
// Umbraco.Prism's tests/demo/support/tmux-terminal.ts (its own README explains why this replaced
// driving ttyd/xterm.js in the recorded browser: mis-sized text, unpainted grey canvas regions,
// and multi-minute visual freezes that assertions can't catch — a DOM mirror fed from
// capture-pane cannot desync from the real session, needs no focus, and has no canvas to freeze).

const SOCKET = 'wayfinder-umbraco-demo';
const SESSION = 'wayfinder-umbraco-demo-terminal';

// 150×36 at 20px/27px Menlo fills a 1920×1080 frame (minus title bar and padding) almost exactly.
export const TERMINAL_COLS = 150;
export const TERMINAL_ROWS = 36;

function tmux(...args: string[]): string {
  return execFileSync('tmux', ['-L', SOCKET, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
}

/**
 * Kills any previous demo terminal server and starts a fresh session of exactly
 * TERMINAL_COLS×TERMINAL_ROWS, wrapping `script` so everything (including a later claude launch
 * and its one-time consent gate) is captured to `logPath` from the very first command.
 * CLAUDECODE/CLAUDE_CODE_* env vars are stripped so a claude launched inside is a genuinely
 * independent process, not a child session of the recording orchestrator.
 */
export async function startDemoTerminalSession(logPath: string, cwd: string): Promise<void> {
  try {
    tmux('kill-server');
  } catch {
    // No server running — nothing to kill.
  }
  const strippedEnv = Object.fromEntries(
    Object.entries(process.env).filter(
      ([key]) => !/^(CLAUDECODE|CLAUDE_CODE_|AI_AGENT|CLAUDE_EFFORT)/.test(key)
    )
  ) as NodeJS.ProcessEnv;
  const child = spawn(
    'tmux',
    [
      '-L', SOCKET,
      'new-session', '-d', '-s', SESSION,
      '-x', String(TERMINAL_COLS), '-y', String(TERMINAL_ROWS),
      '-c', cwd,
      'script', '-q', '-F', logPath, 'bash'
    ],
    { env: strippedEnv, stdio: 'ignore' }
  );
  child.unref();
  const deadline = Date.now() + 5_000;
  for (;;) {
    try {
      tmux('set-option', '-g', 'status', 'off');
      break;
    } catch {
      if (Date.now() > deadline) throw new Error('tmux demo server did not start in time');
    }
  }

  await waitForPaneStable();
}

/**
 * Polls capture-pane until its content is non-empty AND identical across two consecutive reads
 * ~100ms apart (genuinely settled, not just present), or the timeout elapses (best-effort —
 * returns rather than hanging forever). Confirmed live this is needed before EVERY send in a
 * setup sequence, not just the very first one after session creation: the tmux pane being up (or
 * a previous command having returned) doesn't mean the shell has actually redrawn its prompt and
 * is reading stdin yet — a send-keys call made too soon loses its leading character(s) ("claude"
 * arrived as "laude" once right after session creation; "claude mcp add ..." arrived as "laude
 * mcp add ..." a separate time, right after an unrelated `claude mcp remove ...` had just
 * returned) — silently corrupting a setup step with no visible error. Matches this harness's own
 * "poll real state, don't fixed-sleep" convention rather than a blind delay that either races on
 * a slow machine or wastes time on a fast one.
 */
export async function waitForPaneStable(timeoutMs = 5_000): Promise<void> {
  // Two consecutive identical reads was NOT a strong enough signal on its own — confirmed live,
  // even with this check in place before every send, large leading chunks of the next command
  // still occasionally arrived dropped (e.g. "claude mcp add --transport http ..." landing as
  // just "ort http ..."). An idle prompt with genuinely nothing left to print reads as "stable"
  // on the very first poll, before the shell has actually finished settling at the OS/pty level
  // — passing this check instantly gives back none of the real settle time a slower case would
  // have picked up incidentally. Requiring THREE consecutive stable reads, spaced further apart,
  // forces a real minimum wall-clock window (~450ms) to elapse even in the instant-stable case.
  const requiredStableReads = 3;
  let stableCount = 0;
  let previous: string | null = null;
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const current = captureTerminal();
    if (current.trim() && current === previous) {
      stableCount++;
      if (stableCount >= requiredStableReads) return;
    } else {
      stableCount = 0;
    }
    previous = current;
    if (Date.now() > deadline) return;
    await new Promise(resolve => setTimeout(resolve, 150));
  }
}

/**
 * Sends the full text to the session as ONE atomic `send-keys` call — this can never drop or
 * corrupt a leading (or mid-string) character the way per-character sends could, because there's
 * no per-character race against the pty/shell to lose. Confirmed live, twice, that even a
 * readiness poll before sending wasn't enough to fully eliminate the old per-character approach's
 * race: "claude" arrived as "laude" right after session creation, and separately "claude mcp add
 * ..." arrived with a large leading chunk missing right after an unrelated command had just
 * returned. A single `-l` argument also sidesteps the old lone-";" escaping problem for free —
 * that only occurred because each character was passed as its own separate argv element; a
 * semicolon embedded inside one already-split multi-character argument is never re-parsed by
 * tmux's own command-line splitter.
 *
 * `cosmeticTypingDelayMs` (optional) reveals the newly-appeared text character-by-character in
 * the recorded page's own DOM mirror ONLY — a purely visual animation layered on top of content
 * that has already fully and reliably landed in the real terminal, never a re-drive of the real
 * input. Falls back to an instant reveal (no animation) if the pane's before/after content isn't
 * a clean append (e.g. a line wrap or scroll happened) rather than risk animating a wrong diff.
 */
export async function sendTerminalText(text: string, cosmeticTypingDelayMs = 0): Promise<void> {
  const before = captureTerminal();
  tmux('send-keys', '-t', SESSION, '-l', '--', text);
  if (cosmeticTypingDelayMs > 0) {
    await animateReveal(before, cosmeticTypingDelayMs);
  }
}

/** Sends a named key (Enter, Escape, C-c, ...) to the session. */
export function sendTerminalKey(key: string): void {
  tmux('send-keys', '-t', SESSION, key);
}

/**
 * Polls the LIVE pane content (never a rolling session-log tail) until `pattern` matches, or the
 * timeout elapses. Checking the live pane specifically — not an ever-growing log file — matters:
 * confirmed live, a rolling tail of the log can still contain a gate's own option text well after
 * that gate has actually been dismissed and the pane has moved on, which previously caused a
 * stray answer to get sent into whatever's genuinely on screen by that point (a real, blocking
 * consent gate's option text overlapped with an already-handled EARLIER gate's option text — "No,
 * exit" appears in both the trust-folder gate and the BypassPermissions gate, so a rolling-buffer
 * match on it alone couldn't tell "still showing" from "already handled and now stale").
 */
export async function waitForPromptText(pattern: RegExp, timeoutMs: number): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    if (pattern.test(stripAnsiForMatching(captureTerminal()))) return true;
    if (Date.now() > deadline) return false;
    await new Promise(resolve => setTimeout(resolve, 200));
  }
}

/** The inverse of waitForPromptText — polls until `pattern` no longer matches the live pane. */
export async function waitForPromptTextGone(pattern: RegExp, timeoutMs: number): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    if (!pattern.test(stripAnsiForMatching(captureTerminal()))) return;
    if (Date.now() > deadline) return;
    await new Promise(resolve => setTimeout(resolve, 200));
  }
}

/** The visible pane content, with SGR colour escapes preserved for the mirror to render. */
export function captureTerminal(): string {
  return tmux('capture-pane', '-p', '-e', '-t', SESSION);
}

function cursorPosition(): { x: number; y: number; visible: boolean } {
  try {
    const raw = tmux('display-message', '-p', '-t', SESSION, '#{cursor_x} #{cursor_y} #{cursor_flag}').trim();
    const [x, y, flag] = raw.split(/\s+/).map(Number);
    return { x: x || 0, y: y || 0, visible: flag !== 0 };
  } catch {
    return { x: 0, y: 0, visible: false };
  }
}

/**
 * Strips ALL ANSI escape sequences (not just SGR colour codes — cursor-positioning ones too) so
 * a plain phrase match can be run against the result. Confirmed live this is necessary, not
 * just tidy: a full-screen TUI prompt (Claude Code's own trust/consent gates) writes each WORD
 * with its own absolute cursor-position escape in between ("Yes," <esc>[12G "I" <esc>[14G
 * "trust" ...), so a phrase like "trust this folder" never appears as one contiguous substring
 * in the raw captured log — a naive regex against the raw bytes silently never matches. Each
 * escape is replaced with a single space, not deleted outright — deleting it glues adjacent
 * words together with no separator at all ("trustthisfolder"), which fails a phrase match just
 * as badly; the trailing whitespace-collapse then normalises however many spaces that leaves.
 */
export function stripAnsiForMatching(raw: string): string {
  return raw
    .replace(/\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)/g, ' ')
    .replace(/\x1b\[[0-9;?]*[A-Za-z]/g, ' ')
    .replace(/\r/g, ' ')
    .replace(/\s+/g, ' ');
}

// ------------------------------------------------------------------ ANSI → HTML

const BASE_COLORS = [
  '#21262d', '#f47067', '#57ab5a', '#c69026', '#539bf5', '#b083f0', '#39c5cf', '#adbac7',
  '#545d68', '#ff938a', '#6bc46d', '#daaa3f', '#6cb6ff', '#dcbdfb', '#56d4dd', '#cdd9e5'
];

function xterm256(n: number): string {
  if (n < 16) return BASE_COLORS[n];
  if (n < 232) {
    const idx = n - 16;
    const steps = [0, 95, 135, 175, 215, 255];
    const r = steps[Math.floor(idx / 36)];
    const g = steps[Math.floor(idx / 6) % 6];
    const b = steps[idx % 6];
    return `rgb(${r},${g},${b})`;
  }
  const v = 8 + (n - 232) * 10;
  return `rgb(${v},${v},${v})`;
}

interface SgrState {
  bold: boolean;
  dim: boolean;
  italic: boolean;
  underline: boolean;
  reverse: boolean;
  fg: string | null;
  bg: string | null;
}

function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function styleFor(state: SgrState): string {
  let fg = state.fg;
  let bg = state.bg;
  if (state.reverse) {
    [fg, bg] = [bg ?? '#e6edf3', fg ?? '#101418'];
  }
  const parts: string[] = [];
  if (fg) parts.push(`color:${fg}`);
  if (bg) parts.push(`background:${bg}`);
  if (state.bold) parts.push('font-weight:600');
  if (state.dim) parts.push('opacity:.62');
  if (state.italic) parts.push('font-style:italic');
  if (state.underline) parts.push('text-decoration:underline');
  return parts.join(';');
}

function applySgr(state: SgrState, params: number[]): void {
  for (let i = 0; i < params.length; i++) {
    const p = params[i];
    if (p === 0) Object.assign(state, { bold: false, dim: false, italic: false, underline: false, reverse: false, fg: null, bg: null });
    else if (p === 1) state.bold = true;
    else if (p === 2) state.dim = true;
    else if (p === 3) state.italic = true;
    else if (p === 4) state.underline = true;
    else if (p === 7) state.reverse = true;
    else if (p === 22) { state.bold = false; state.dim = false; }
    else if (p === 23) state.italic = false;
    else if (p === 24) state.underline = false;
    else if (p === 27) state.reverse = false;
    else if (p >= 30 && p <= 37) state.fg = BASE_COLORS[p - 30 + (state.bold ? 8 : 0)];
    else if (p === 38 && params[i + 1] === 5) { state.fg = xterm256(params[i + 2] ?? 0); i += 2; }
    else if (p === 38 && params[i + 1] === 2) { state.fg = `rgb(${params[i + 2] ?? 0},${params[i + 3] ?? 0},${params[i + 4] ?? 0})`; i += 4; }
    else if (p === 39) state.fg = null;
    else if (p >= 40 && p <= 47) state.bg = BASE_COLORS[p - 40];
    else if (p === 48 && params[i + 1] === 5) { state.bg = xterm256(params[i + 2] ?? 0); i += 2; }
    else if (p === 48 && params[i + 1] === 2) { state.bg = `rgb(${params[i + 2] ?? 0},${params[i + 3] ?? 0},${params[i + 4] ?? 0})`; i += 4; }
    else if (p === 49) state.bg = null;
    else if (p >= 90 && p <= 97) state.fg = BASE_COLORS[p - 90 + 8];
    else if (p >= 100 && p <= 107) state.bg = BASE_COLORS[p - 100 + 8];
  }
}

/** Converts capture-pane -e output (text + SGR escapes only) into styled HTML. */
export function ansiToHtml(raw: string): string {
  const cleaned = raw
    .replace(/\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)/g, '')
    .replace(/\x1b\[[0-9;?]*[A-LN-Za-ln-z]/g, '');
  const state: SgrState = { bold: false, dim: false, italic: false, underline: false, reverse: false, fg: null, bg: null };
  let html = '';
  let last = 0;
  const sgr = /\x1b\[([0-9;]*)m/g;
  const emit = (text: string) => {
    if (!text) return;
    const style = styleFor(state);
    html += style ? `<span style="${style}">${escapeHtml(text)}</span>` : escapeHtml(text);
  };
  for (let match = sgr.exec(cleaned); match; match = sgr.exec(cleaned)) {
    emit(cleaned.slice(last, match.index));
    applySgr(state, match[1].split(';').filter(Boolean).map(Number));
    last = match.index + match[0].length;
  }
  emit(cleaned.slice(last));
  return html;
}

// ------------------------------------------------------------------ mirror lifecycle

let mirrorTimer: ReturnType<typeof setInterval> | null = null;
let lastFrame = '';
let activePage: Page | null = null;
let animating = false;

/**
 * Cosmetic-only character reveal for text just sent atomically via sendTerminalText — the real
 * terminal already has the full content by the time this runs; this only controls what the
 * recorded page's DOM mirror shows, and when. Sets `animating` so the normal poll loop (below)
 * steps aside while this drives the DOM directly, restoring it afterward so real content (cursor
 * blink, later output) resumes updating normally.
 */
async function animateReveal(beforeRaw: string, delayMs: number): Promise<void> {
  if (!activePage) return;
  // send-keys returns once tmux has queued the write, not once the shell/pty has actually
  // processed and echoed it — give that a brief moment before diffing.
  await new Promise(resolve => setTimeout(resolve, 60));
  const afterRaw = captureTerminal();
  if (!afterRaw.startsWith(beforeRaw)) {
    // Not a clean append (a line wrap or scroll shifted earlier content) — safe fallback: let
    // the normal poll show the final state instantly rather than animate a wrong diff.
    return;
  }
  const newPortion = afterRaw.slice(beforeRaw.length);
  if (!newPortion.trim()) return;

  animating = true;
  try {
    for (let i = 1; i <= newPortion.length; i++) {
      const html = ansiToHtml((beforeRaw + newPortion.slice(0, i)).replace(/\n$/, ''));
      await activePage.evaluate((innerHtml) => {
        const content = document.getElementById('demo-terminal-content');
        if (content) content.innerHTML = innerHtml;
      }, html);
      await new Promise(resolve => setTimeout(resolve, delayMs));
    }
  } finally {
    animating = false;
    lastFrame = ''; // force the real poll to resync against actual pane state on its next tick
  }
}

async function installMirrorChrome(page: Page): Promise<void> {
  await page.evaluate(({ cols }) => {
    if (document.getElementById('demo-terminal')) return;
    document.body.style.margin = '0';
    document.body.style.background = '#0b0e13';
    const root = document.createElement('div');
    root.id = 'demo-terminal';
    Object.assign(root.style, {
      display: 'flex', flexDirection: 'column', height: '100vh', overflow: 'hidden',
      background: '#101418'
    } satisfies Partial<CSSStyleDeclaration>);

    const bar = document.createElement('div');
    Object.assign(bar.style, {
      display: 'flex', alignItems: 'center', gap: '8px', padding: '0 18px', height: '44px',
      background: '#1a2029', flex: '0 0 auto',
      font: '500 15px/1 -apple-system, "Segoe UI", system-ui, sans-serif', color: '#768390'
    } satisfies Partial<CSSStyleDeclaration>);
    for (const color of ['#f47067', '#c69026', '#57ab5a']) {
      const light = document.createElement('span');
      Object.assign(light.style, {
        width: '13px', height: '13px', borderRadius: '50%', background: color, flex: '0 0 auto'
      } satisfies Partial<CSSStyleDeclaration>);
      bar.appendChild(light);
    }
    const title = document.createElement('span');
    title.textContent = `bash — ${cols} cols`;
    title.style.marginLeft = '12px';
    bar.appendChild(title);
    root.appendChild(bar);

    const wrapper = document.createElement('div');
    Object.assign(wrapper.style, {
      position: 'relative', flex: '1 1 auto', padding: '20px 28px', overflow: 'hidden'
    } satisfies Partial<CSSStyleDeclaration>);
    const content = document.createElement('pre');
    content.id = 'demo-terminal-content';
    Object.assign(content.style, {
      margin: '0',
      font: '400 20px/27px "SF Mono", ui-monospace, Menlo, Consolas, monospace',
      color: '#e6edf3', whiteSpace: 'pre', position: 'relative'
    } satisfies Partial<CSSStyleDeclaration>);
    const cursor = document.createElement('div');
    cursor.id = 'demo-terminal-cursor';
    Object.assign(cursor.style, {
      position: 'absolute', width: '1ch', height: '27px',
      background: 'rgba(230, 237, 243, 0.45)', borderRadius: '2px',
      font: '400 20px/27px "SF Mono", ui-monospace, Menlo, Consolas, monospace',
      pointerEvents: 'none', transition: 'left 60ms linear, top 60ms linear'
    } satisfies Partial<CSSStyleDeclaration>);
    wrapper.appendChild(content);
    wrapper.appendChild(cursor);
    root.appendChild(wrapper);
    document.body.appendChild(root);
  }, { cols: TERMINAL_COLS });
}

/**
 * Navigates the recorded page to the terminal mirror (installing it if needed) and starts the
 * capture-pane poll. Idempotent — safe to call again in a later act on the same page.
 */
export async function showTerminalMirror(page: Page): Promise<void> {
  activePage = page;
  if (!page.url().startsWith('about:blank')) {
    await page.goto('about:blank');
  }
  await installMirrorChrome(page);
  lastFrame = '';
  if (mirrorTimer) return;
  let busy = false;
  mirrorTimer = setInterval(() => {
    if (busy || animating) return;
    busy = true;
    void (async () => {
      try {
        const raw = captureTerminal();
        const cursor = cursorPosition();
        const frameKey = `${raw}@${cursor.x},${cursor.y}`;
        if (frameKey !== lastFrame) {
          lastFrame = frameKey;
          const html = ansiToHtml(raw.replace(/\n$/, ''));
          await page.evaluate(({ html, cursor }) => {
            const content = document.getElementById('demo-terminal-content');
            const cursorEl = document.getElementById('demo-terminal-cursor');
            if (!content || !cursorEl) return;
            content.innerHTML = html;
            cursorEl.style.display = cursor.visible ? 'block' : 'none';
            cursorEl.style.left = `calc(28px + ${cursor.x}ch)`;
            cursorEl.style.top = `${20 + cursor.y * 27}px`;
          }, { html, cursor });
        }
      } catch {
        // Page navigating or capture hiccup — the next tick will catch up.
      }
      busy = false;
    })();
  }, 300);
}

/** Stops the mirror poll — call when the recording is done with the terminal for good. */
export function stopTerminalMirror(): void {
  if (mirrorTimer) {
    clearInterval(mirrorTimer);
    mirrorTimer = null;
  }
}
