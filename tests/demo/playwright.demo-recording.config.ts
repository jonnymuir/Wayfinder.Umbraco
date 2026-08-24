import { defineConfig } from '@playwright/test';

// Not a test config — a recording tool. Deliberately excluded from CI: no npm script or workflow
// references this file, and its spec filename doesn't match any CI-facing config's testMatch.
export default defineConfig({
  testDir: '.',
  testMatch: /mcp-authoring-demo\.spec\.ts/,
  globalSetup: './support/demo-prereqs-setup.ts',
  fullyParallel: false,
  workers: 1,
  // Per-test default — Act 2 (the real agent call) overrides its own timeout via
  // test.setTimeout() so other acts fail fast on a stuck selector instead of burning the whole
  // budget.
  timeout: 5 * 60_000,
  expect: { timeout: 30_000 },
  use: {
    baseURL: 'https://localhost:44399',
    ignoreHTTPSErrors: true,
    // Headless Chromium throttles rendering on a backgrounded tab — confirmed elsewhere in this
    // product line to visually freeze the recorded video on one frame for tens of minutes while
    // the underlying automation kept working. headed is what actually fixes it.
    headless: false,
    // The spec creates and records its own single page in beforeAll (one continuous video across
    // every act) rather than destructuring Playwright's per-test page/video fixtures.
    trace: 'off'
  }
});
