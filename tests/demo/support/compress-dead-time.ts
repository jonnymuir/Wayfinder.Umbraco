import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

/**
 * Post-processing pass over an already-recorded demo take: speeds up the stretches the spec
 * itself marked (via markWaitStart/markWaitEnd in ./narration.ts) as real dead air — waiting on a
 * live external process (the agent's own design work, gaps before the live "designer" answers a
 * question) with nothing narrated for a viewer to read. Deliberately NOT generic pixel/frame-diff
 * "dead air" detection: a narration slate that holds still for its own reading-paced duration
 * would be indistinguishable from real dead air to a motion detector, but here there's no
 * ambiguity, because the spec already timestamped exactly which stretches qualify. Everything
 * outside a marked wait segment (all of Acts 1/3/4/5, and the narrated portions of Act 2) passes
 * through completely untouched, at its original speed and duration.
 *
 * Runs after the fact, over the finished video file — not live during the recording — because the
 * real wall-clock time being waited on (a live `claude` CLI process, a live LLM answering as the
 * "designer") is external and can't be sped up without corrupting the take itself.
 */

export interface WaitSegment {
  startMs: number;
  endMs: number;
  label: string;
}

interface PlanSegment {
  startMs: number;
  endMs: number;
  /** Playback speed multiplier applied to this segment; 1 = untouched, >1 = sped up. */
  speedFactor: number;
}

// 12x is fast enough to read as "we skipped ahead" rather than "we cut a chunk out", while still
// giving a viewer a few frames of the sped-up terminal/browser activity as a visual "yes, work IS
// happening here" cue rather than a hard jump cut straight past it.
const SPEED_FACTOR = 12;
// Even a short wait segment stays visible for at least this long, sped up — so a genuine but brief
// stall doesn't vanish into an imperceptible flash.
const MIN_COMPRESSED_MS = 3_000;
// One very long wait (the live design conversation can legitimately run many minutes) is capped
// here rather than left to shrink proportionally forever — past this point "sped up further" reads
// as "the video is broken", not "time is passing quickly".
const MAX_COMPRESSED_MS = 20_000;

/**
 * Turns the spec's raw wait segments into an ordered, gap-filling plan covering the entire video:
 * alternating untouched "keep" segments and sped-up "wait" segments, clamped to [0, videoDurationMs].
 * Overlapping/out-of-order input segments are merged/sorted defensively — the spec's own
 * markWaitStart/markWaitEnd pairing should never produce those, but a plan built from bad input
 * should fail loudly in ffmpeg rather than silently mis-cut the video.
 */
export function buildCompressionPlan(rawSegments: readonly WaitSegment[], videoDurationMs: number): PlanSegment[] {
  const clamped = rawSegments
    .map(s => ({ startMs: Math.max(0, Math.min(s.startMs, videoDurationMs)), endMs: Math.max(0, Math.min(s.endMs, videoDurationMs)) }))
    .filter(s => s.endMs > s.startMs)
    .sort((a, b) => a.startMs - b.startMs);

  const merged: Array<{ startMs: number; endMs: number }> = [];
  for (const seg of clamped) {
    const last = merged[merged.length - 1];
    if (last && seg.startMs <= last.endMs) {
      last.endMs = Math.max(last.endMs, seg.endMs);
    } else {
      merged.push({ ...seg });
    }
  }

  const plan: PlanSegment[] = [];
  let cursor = 0;
  for (const wait of merged) {
    if (wait.startMs > cursor) {
      plan.push({ startMs: cursor, endMs: wait.startMs, speedFactor: 1 });
    }
    const rawDurationMs = wait.endMs - wait.startMs;
    const compressedMs = Math.min(MAX_COMPRESSED_MS, Math.max(MIN_COMPRESSED_MS, rawDurationMs / SPEED_FACTOR));
    const actualFactor = rawDurationMs / compressedMs;
    plan.push({ startMs: wait.startMs, endMs: wait.endMs, speedFactor: actualFactor });
    cursor = wait.endMs;
  }
  if (cursor < videoDurationMs) {
    plan.push({ startMs: cursor, endMs: videoDurationMs, speedFactor: 1 });
  }
  return plan;
}

/** Sum of each planned segment's OWN resulting duration after its speedFactor is applied. */
export function planOutputDurationMs(plan: readonly PlanSegment[]): number {
  return plan.reduce((total, seg) => total + (seg.endMs - seg.startMs) / seg.speedFactor, 0);
}

function buildFfmpegArgs(inputPath: string, outputPath: string, plan: readonly PlanSegment[]): string[] {
  const filterParts: string[] = [];
  const labels: string[] = [];
  plan.forEach((seg, i) => {
    const label = `v${i}`;
    labels.push(`[${label}]`);
    const startSec = (seg.startMs / 1000).toFixed(3);
    const endSec = (seg.endMs / 1000).toFixed(3);
    const ptsExpr = seg.speedFactor === 1 ? 'PTS-STARTPTS' : `(PTS-STARTPTS)/${seg.speedFactor}`;
    filterParts.push(`[0:v]trim=start=${startSec}:end=${endSec},setpts=${ptsExpr}[${label}]`);
  });
  filterParts.push(`${labels.join('')}concat=n=${plan.length}:v=1:a=0[outv]`);

  return [
    '-y',
    '-i', inputPath,
    '-filter_complex', filterParts.join(';'),
    '-map', '[outv]',
    '-an',
    '-c:v', 'libx264',
    '-preset', 'medium',
    '-crf', '18',
    outputPath
  ];
}

function ffprobeDurationMs(videoPath: string): number {
  const raw = execFileSync(
    'ffprobe',
    ['-v', 'error', '-show_entries', 'format=duration', '-of', 'default=noprint_wrappers=1:nokey=1', videoPath],
    { encoding: 'utf8' }
  );
  return Math.round(parseFloat(raw.trim()) * 1000);
}

export interface CompressDeadTimeResult {
  outputPath: string;
  inputDurationMs: number;
  outputDurationMs: number;
  waitSegmentCount: number;
  waitMsCompressed: number;
}

/**
 * Reads waitSegmentsPath (the JSON written by the spec's afterAll, from narration.ts's
 * getWaitSegments()), applies the compression plan to videoPath via ffmpeg, and writes the result
 * to outputPath — a distinctly-named file, never overwriting videoPath itself, so the raw take is
 * always still on disk regardless of how the compressed pass turns out.
 */
export function compressDeadTime(videoPath: string, waitSegmentsPath: string, outputPath: string): CompressDeadTimeResult {
  const rawSegments: WaitSegment[] = JSON.parse(readFileSync(waitSegmentsPath, 'utf8'));
  const inputDurationMs = ffprobeDurationMs(videoPath);

  if (rawSegments.length === 0) {
    // Nothing marked as dead air — copy through unchanged rather than running a no-op filter
    // graph, and rather than silently producing "compressed" output identical to the input.
    execFileSync('ffmpeg', ['-y', '-i', videoPath, '-c', 'copy', '-an', outputPath], { stdio: 'ignore' });
    return { outputPath, inputDurationMs, outputDurationMs: inputDurationMs, waitSegmentCount: 0, waitMsCompressed: 0 };
  }

  const plan = buildCompressionPlan(rawSegments, inputDurationMs);
  const args = buildFfmpegArgs(videoPath, outputPath, plan);
  execFileSync('ffmpeg', args, { stdio: 'ignore' });

  const outputDurationMs = ffprobeDurationMs(outputPath);
  const waitMsCompressed = rawSegments.reduce((total, s) => total + Math.max(0, s.endMs - s.startMs), 0);
  return { outputPath, inputDurationMs, outputDurationMs, waitSegmentCount: rawSegments.length, waitMsCompressed };
}

// CLI entry point for standalone validation against an already-recorded video, without needing a
// new real take: `node compress-dead-time.ts <videoPath> <waitSegmentsPath> <outputPath>`.
const isCliEntry = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isCliEntry) {
  const [videoPath, waitSegmentsPath, outputPath] = process.argv.slice(2);
  if (!videoPath || !waitSegmentsPath || !outputPath) {
    console.error('Usage: node compress-dead-time.ts <videoPath> <waitSegmentsPath> <outputPath>');
    process.exit(1);
  }
  const result = compressDeadTime(videoPath, waitSegmentsPath, outputPath);
  console.log(JSON.stringify(result, null, 2));
}
