import type { Page } from '@playwright/test';

// Professional lower-third + full-screen slate system for the demo recording, so the story always
// reads "here's what we have -> here's what we'll do -> [it happens] -> here's what just
// happened", with hold time computed from the text itself (word count / spoken pace) rather than
// a fixed flash. Ported from Umbraco.Prism's tests/demo/support/narration.ts.

export type BeatKind = 'setup' | 'intent' | 'recap' | 'note';

const BEAT_LABEL: Record<BeatKind, string> = {
  setup: 'WHAT WE HAVE',
  intent: "WHAT WE'RE ABOUT TO DO",
  recap: 'WHAT JUST HAPPENED',
  note: ''
};

const BEAT_ACCENT: Record<BeatKind, string> = {
  setup: '#7dd3fc',
  intent: '#facc15',
  recap: '#86efac',
  note: '#e5e7eb'
};

// ~2.6 words/sec is a comfortable, unhurried spoken pace — faster than that and a presenter
// reading the caption aloud is racing the fade-out.
const READING_MS_PER_WORD = 380;
const MIN_HOLD_MS = 3200;
const MAX_HOLD_MS = 5000;

function computeHoldMs(text: string): number {
  const words = text.trim().split(/\s+/).filter(Boolean).length;
  return Math.min(MAX_HOLD_MS, Math.max(MIN_HOLD_MS, Math.round(words * READING_MS_PER_WORD)));
}

export interface NarrationTimelineEntry {
  atMs: number;
  kind: string;
  text: string;
  holdMs: number;
}

/**
 * A genuinely dead-air stretch — no narration beat covering it, nothing for a viewer to read,
 * real wall-clock time spent waiting on something external (a live agent conversation) that
 * can't be sped up live without corrupting the take. Recorded explicitly by the spec bracketing
 * the actual wait with markWaitStart/markWaitEnd, rather than inferred after the fact from video
 * pixels — a narration slate that happens to hold still for its own reading-paced duration would
 * be indistinguishable from real dead air to a generic frame-diff/motion detector, but here
 * there's no ambiguity: only a stretch the spec itself marks as "nothing narrated, waiting on an
 * external process" is a wait segment.
 */
export interface WaitSegment {
  startMs: number;
  endMs: number;
  label: string;
}

let recordingStartedAt: number | null = null;
const timeline: NarrationTimelineEntry[] = [];
const waitSegments: WaitSegment[] = [];
let openWait: { startMs: number; label: string } | null = null;

export function startNarrationTimeline(): void {
  recordingStartedAt = Date.now();
  timeline.length = 0;
  waitSegments.length = 0;
  openWait = null;
}

export function getNarrationTimeline(): readonly NarrationTimelineEntry[] {
  return timeline;
}

export function getWaitSegments(): readonly WaitSegment[] {
  return waitSegments;
}

/** Marks the start of a genuinely dead-air stretch (see WaitSegment). Must be paired with a
 * later markWaitEnd() before the recording finishes — an unclosed wait is dropped rather than
 * guessed at, since guessing its end would risk compressing real narrated content that follows. */
export function markWaitStart(label: string): void {
  if (recordingStartedAt === null || openWait !== null) return;
  openWait = { startMs: Date.now() - recordingStartedAt, label };
}

/** Marks the end of the most recently started wait segment. A no-op if none is open. */
export function markWaitEnd(): void {
  if (recordingStartedAt === null || openWait === null) return;
  const endMs = Date.now() - recordingStartedAt;
  if (endMs > openWait.startMs) {
    waitSegments.push({ startMs: openWait.startMs, endMs, label: openWait.label });
  }
  openWait = null;
}

function recordTimelineEntry(kind: string, text: string, holdMs: number): void {
  if (recordingStartedAt === null) {
    return;
  }
  timeline.push({ atMs: Date.now() - recordingStartedAt, kind, text, holdMs });
}

/**
 * Several beats fire immediately after a real navigation — waitForLoadState('networkidle')
 * doesn't fully rule out a trailing redirect still tearing down the document, which kills
 * page.evaluate's execution context mid-call. Retry once after a short settle rather than failing
 * the whole act over late-arriving navigation.
 */
async function evaluateResilient<Args>(page: Page, fn: (args: Args) => void, args: Args): Promise<void> {
  for (let attempt = 0; ; attempt++) {
    try {
      await page.evaluate(fn as Parameters<Page['evaluate']>[0], args);
      return;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      if (attempt >= 2 || !/execution context|context was destroyed/i.test(message)) {
        throw error;
      }
      await page.waitForTimeout(300);
    }
  }
}

export type NarrationPosition = 'top' | 'bottom';

const POSITION_TOP: Record<NarrationPosition, string> = {
  top: '5%',
  bottom: 'calc(100% - 160px)'
};

/**
 * Show one narration beat as a lower-third (or upper-third) bar and hold for a reading-paced
 * duration. Awaiting this call is the pacing primitive for the whole recording.
 */
export async function beat(
  page: Page,
  kind: BeatKind,
  text: string,
  opts: { holdMs?: number; position?: NarrationPosition } = {}
): Promise<void> {
  await page.bringToFront().catch(() => {});
  const hold = opts.holdMs ?? computeHoldMs(text);
  const position = opts.position ?? 'bottom';
  recordTimelineEntry(kind, text, hold);
  await evaluateResilient(
    page,
    ({ text, label, accent, top }) => {
      const id = 'demo-narration';
      let bar = document.getElementById(id) as HTMLDivElement | null;
      if (!bar) {
        bar = document.createElement('div');
        bar.id = id;
        Object.assign(bar.style, {
          position: 'fixed',
          left: '50%',
          top,
          transform: 'translateX(-50%)',
          width: 'min(80%, 1200px)',
          background: 'rgba(15, 20, 30, 0.88)',
          color: '#ffffff',
          font: '500 27px/1.45 -apple-system, "Segoe UI", system-ui, sans-serif',
          padding: '20px 30px',
          borderRadius: '10px',
          zIndex: '2147483647',
          textAlign: 'left',
          boxShadow: '0 8px 30px rgba(0,0,0,0.35)',
          opacity: '0',
          transition: 'opacity 220ms ease, top 550ms cubic-bezier(0.4, 0, 0.2, 1)',
          pointerEvents: 'none'
        } satisfies Partial<CSSStyleDeclaration>);
        const labelEl = document.createElement('div');
        labelEl.id = `${id}-label`;
        Object.assign(labelEl.style, {
          font: '700 14px/1 -apple-system, "Segoe UI", system-ui, sans-serif',
          letterSpacing: '0.12em',
          marginBottom: '8px'
        } satisfies Partial<CSSStyleDeclaration>);
        const textEl = document.createElement('div');
        textEl.id = `${id}-text`;
        bar.appendChild(labelEl);
        bar.appendChild(textEl);
        document.body.appendChild(bar);
      } else {
        bar.style.top = top;
      }
      const labelEl = document.getElementById(`${id}-label`)!;
      const textEl = document.getElementById(`${id}-text`)!;
      labelEl.textContent = label;
      labelEl.style.color = accent;
      labelEl.style.display = label ? 'block' : 'none';
      textEl.textContent = text;
      requestAnimationFrame(() => {
        bar!.style.opacity = '1';
      });
    },
    { text, label: BEAT_LABEL[kind], accent: BEAT_ACCENT[kind], top: POSITION_TOP[position] }
  );
  await page.waitForTimeout(hold);
}

/** Smoothly slide the narration bar between its top and bottom anchors without changing text. */
export async function moveNarrationTo(page: Page, position: NarrationPosition, settleMs = 620): Promise<void> {
  await evaluateResilient(page, top => {
    const bar = document.getElementById('demo-narration');
    if (bar) bar.style.top = top;
  }, POSITION_TOP[position]);
  await page.waitForTimeout(settleMs);
}

/** Fade the lower-third out. */
export async function clearBeat(page: Page): Promise<void> {
  await evaluateResilient(page, () => {
    const bar = document.getElementById('demo-narration');
    if (bar) bar.style.opacity = '0';
  }, undefined);
  await page.waitForTimeout(260);
}

/**
 * Full-screen title slate — cold open and closing recap, and (via bodyStyle) a dense,
 * multi-paragraph read like the design brief. bodyStyle merges onto the body element's default
 * styling (centered, no line-break preservation) — the brief call below overrides whiteSpace/
 * textAlign/maxWidth/fontSize for a genuinely readable left-aligned block; other callers are
 * unaffected since they don't pass it.
 */
export async function showSlate(
  page: Page,
  opts: { eyebrow?: string; title: string; body: string; holdMs?: number; bodyStyle?: Partial<CSSStyleDeclaration> }
): Promise<void> {
  const slateHold = opts.holdMs ?? computeHoldMs(`${opts.title} ${opts.body}`) + 1500;
  recordTimelineEntry('slate', `${opts.title}. ${opts.body}`, slateHold);
  await evaluateResilient(
    page,
    ({ eyebrow, title, body, bodyStyle }) => {
      const id = 'demo-slate';
      document.getElementById(id)?.remove();
      const slate = document.createElement('div');
      slate.id = id;
      Object.assign(slate.style, {
        position: 'fixed',
        inset: '0',
        background: 'linear-gradient(160deg, #0b1220 0%, #111827 100%)',
        color: '#f8fafc',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        textAlign: 'center',
        padding: '5vh 8vw',
        zIndex: '2147483647',
        opacity: '0',
        transition: 'opacity 320ms ease',
        font: '400 20px/1.6 -apple-system, "Segoe UI", system-ui, sans-serif'
      } satisfies Partial<CSSStyleDeclaration>);

      if (eyebrow) {
        const eyebrowEl = document.createElement('div');
        eyebrowEl.textContent = eyebrow;
        Object.assign(eyebrowEl.style, {
          font: '700 15px/1 -apple-system, "Segoe UI", system-ui, sans-serif',
          letterSpacing: '0.16em',
          color: '#7dd3fc',
          marginBottom: '18px'
        } satisfies Partial<CSSStyleDeclaration>);
        slate.appendChild(eyebrowEl);
      }

      const titleEl = document.createElement('div');
      titleEl.textContent = title;
      Object.assign(titleEl.style, {
        font: '700 44px/1.25 -apple-system, "Segoe UI", system-ui, sans-serif',
        maxWidth: '900px',
        marginBottom: '22px'
      } satisfies Partial<CSSStyleDeclaration>);
      slate.appendChild(titleEl);

      const bodyEl = document.createElement('div');
      bodyEl.textContent = body;
      Object.assign(bodyEl.style, {
        maxWidth: '820px',
        fontSize: '25px',
        color: '#f4f6f8'
      } satisfies Partial<CSSStyleDeclaration>);
      Object.assign(bodyEl.style, bodyStyle ?? {});
      slate.appendChild(bodyEl);

      document.body.appendChild(slate);
      requestAnimationFrame(() => {
        slate.style.opacity = '1';
      });
    },
    { eyebrow: opts.eyebrow, title: opts.title, body: opts.body, bodyStyle: opts.bodyStyle }
  );
  await page.waitForTimeout(slateHold);
}

/** Fade the slate out and remove it. */
export async function clearSlate(page: Page): Promise<void> {
  await evaluateResilient(page, () => {
    const slate = document.getElementById('demo-slate');
    if (slate) slate.style.opacity = '0';
  }, undefined);
  await page.waitForTimeout(340);
  await evaluateResilient(page, () => document.getElementById('demo-slate')?.remove(), undefined);
}

/**
 * A small persistent corner chip shown for the length of a dead-air stretch (see WaitSegment) —
 * so once the post-processing pass speeds that stretch up, the sped-up frames read clearly as
 * "we're moving through waiting time" rather than a glitch. Distinct from the narration bar: it
 * carries no story, just a status, and it stays put until hideFastForwardChip() removes it. Sits
 * bottom-right, clear of the narration bar's top/bottom anchors.
 */
export async function showFastForwardChip(page: Page, text: string): Promise<void> {
  await evaluateResilient(page, label => {
    const id = 'demo-fastforward';
    let chip = document.getElementById(id) as HTMLDivElement | null;
    if (!chip) {
      chip = document.createElement('div');
      chip.id = id;
      Object.assign(chip.style, {
        position: 'fixed',
        right: '32px',
        bottom: '32px',
        background: 'rgba(180, 83, 9, 0.92)',
        color: '#fff7ed',
        font: '600 18px/1 -apple-system, "Segoe UI", system-ui, sans-serif',
        padding: '12px 18px',
        borderRadius: '999px',
        zIndex: '2147483646',
        boxShadow: '0 6px 20px rgba(0,0,0,0.35)',
        pointerEvents: 'none',
        opacity: '0',
        transition: 'opacity 200ms ease'
      } satisfies Partial<CSSStyleDeclaration>);
      document.body.appendChild(chip);
    }
    chip.textContent = `⏩  ${label}`;
    requestAnimationFrame(() => {
      chip!.style.opacity = '1';
    });
  }, text);
}

/** Remove the fast-forward chip. A no-op if none is showing. */
export async function hideFastForwardChip(page: Page): Promise<void> {
  await evaluateResilient(page, () => {
    const chip = document.getElementById('demo-fastforward');
    if (chip) chip.remove();
  }, undefined);
}
