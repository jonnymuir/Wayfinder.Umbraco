import { defineConfig } from '@playwright/test';

// Not a test config — a screenshot tool for the repo README and docs. Deliberately excluded from
// CI: no npm script or workflow other than `demo:screenshots` references this file, and its spec
// filename doesn't match any CI-facing config's testMatch. Assumes Wayfinder.Umbraco.ReferenceApp
// is already running on https://localhost:44399 (globalSetup checks, same as the recording tool).
export default defineConfig({
  testDir: '.',
  testMatch: /screenshots\.spec\.ts/,
  globalSetup: './support/demo-prereqs-setup.ts',
  fullyParallel: false,
  workers: 1,
  timeout: 3 * 60_000,
  expect: { timeout: 30_000 },
  use: {
    baseURL: 'https://localhost:44399',
    ignoreHTTPSErrors: true,
    headless: true,
    trace: 'off'
  }
});
