import type { Browser, Page } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { existsSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { getNarrationTimeline, getWaitSegments, startNarrationTimeline, stopTerminalMirror } from 'wayfinder-demo-recording-kit';
import { compressDeadTime } from 'wayfinder-demo-recording-kit/compress-dead-time';
import { footageDir } from './demo-config.js';

function tryConvertToMp4(webmPath: string): void {
  const mp4Path = webmPath.replace(/\.webm$/, '.mp4');
  try {
    execFileSync('ffmpeg', ['-y', '-i', webmPath, '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-c:a', 'aac', mp4Path], {
      stdio: 'ignore'
    });
    console.log(`Also wrote ${mp4Path}.`);
  } catch {
    console.log('ffmpeg not found on PATH — skipping the .mp4 convenience copy. The .webm is the real output.');
  }
}

/** The single Page every act shares, recording one continuous 1080p video. */
export async function startRecording(browser: Browser): Promise<Page> {
  const recordingSize = { width: 1920, height: 1080 };
  const context = await browser.newContext({
    viewport: recordingSize,
    recordVideo: { dir: footageDir, size: recordingSize },
    ignoreHTTPSErrors: true
  });
  const page = await context.newPage();
  startNarrationTimeline();
  return page;
}

/** Compress dead air (see compress-dead-time.ts) from the .mp4 if ffmpeg produced one, else the raw .webm — a separate, clearly-named output file, never overwriting the raw take. */
function compressWaitSegments(finalPath: string, waitSegmentsPath: string): void {
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

/** Closes the page, saves the video, and writes the narration timeline and the wait segments next to it. */
export async function finishRecording(page: Page | undefined): Promise<void> {
  stopTerminalMirror();
  const video = page?.video();
  await page?.close();
  if (!video) {
    return;
  }

  const finalPath = path.join(footageDir, 'wayfinder-umbraco-mcp-authoring-demo.webm');
  await video.saveAs(finalPath);
  await video.delete();
  tryConvertToMp4(finalPath);
  writeFileSync(path.join(footageDir, 'narration-timeline.json'), JSON.stringify(getNarrationTimeline(), null, 2));
  const waitSegmentsPath = path.join(footageDir, 'wait-segments.json');
  writeFileSync(waitSegmentsPath, JSON.stringify(getWaitSegments(), null, 2));
  compressWaitSegments(finalPath, waitSegmentsPath);
}
