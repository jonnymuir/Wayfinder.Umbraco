import { defineConfig } from '@playwright/test';

// Not a test config, a recording tool. Deliberately excluded from CI: no npm script or workflow
// references this file, and its spec filename doesn't match any CI-facing config's testMatch.
export default defineConfig({
  testDir: '.',
  testMatch: /coaching-register-automate-demo\.spec\.ts/,
  globalSetup: './support/automate-demo-prereqs-setup.ts',
  fullyParallel: false,
  workers: 1,
  timeout: 5 * 60_000,
  expect: { timeout: 30_000 },
  use: {
    baseURL: 'https://localhost:44399',
    ignoreHTTPSErrors: true,
    // No live-agent wait in this demo, so headless rAF throttling isn't the risk it is for
    // mcp-authoring-demo.spec.ts. Headed anyway, for consistency with the rest of this
    // product line's recordings and because it's cheap insurance either way.
    headless: false,
    trace: 'off'
  }
});
