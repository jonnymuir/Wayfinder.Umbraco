import { defineConfig } from '@playwright/test';

// Not a test config, a recording tool. Deliberately excluded from CI: no npm script or workflow
// references this file, and its spec filename doesn't match any CI-facing config's testMatch.
export default defineConfig({
  testDir: '.',
  testMatch: /coaching-register-add-stage-demo\.spec\.ts/,
  globalSetup: './support/demo-prereqs-setup.ts',
  fullyParallel: false,
  workers: 1,
  timeout: 5 * 60_000,
  expect: { timeout: 30_000 },
  use: {
    baseURL: 'https://localhost:44399',
    ignoreHTTPSErrors: true,
    // Real agent wait in Act 2, headless Chromium throttles rendering on a backgrounded tab,
    // confirmed elsewhere in this product line to visually freeze the recorded video while the
    // underlying automation kept working. headed is what actually fixes it.
    headless: false,
    trace: 'off'
  }
});
