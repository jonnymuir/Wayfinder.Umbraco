import type { Page } from '@playwright/test';
import { humanClick, humanType } from 'wayfinder-demo-recording-kit';
import { adminCredentials } from './demo-config.js';

/**
 * Drives an OAuth authorize URL (printed by the Claude CLI) through the recorded backoffice page and
 * returns the redirect URL carrying `?code=`, for pasting back into the CLI.
 *
 * The CLI's redirect URI (http://localhost:33418/callback) has nothing listening in --no-browser
 * mode: the browser nav to it fails, but the request URL carries ?code=. Captured with a predicate
 * started before navigating. If the backoffice needs a fresh login or shows a consent page, handle
 * it; on an already-signed-in admin session the redirect to the loopback callback fires straight
 * away and there's no UI to touch. Returns '' if no callback was captured.
 */
export async function approveAuthorizationInBackoffice(page: Page, authUrl: string): Promise<string> {
  const callbackRequest = page
    .waitForRequest(r => r.url().startsWith('http://localhost:33418/callback'), { timeout: 30_000 })
    .catch(() => null);
  await page.goto(authUrl, { waitUntil: 'commit' }).catch(() => {});

  const raced = await Promise.race([
    callbackRequest.then(r => ({ pending: false as const, req: r })),
    page.waitForTimeout(2_000).then(() => ({ pending: true as const, req: null }))
  ]);
  let callbackReq = raced.req;
  if (raced.pending) {
    if (await page.locator('#username-input').isVisible({ timeout: 4_000 }).catch(() => false)) {
      await humanType(page, page.locator('#username-input'), adminCredentials.email);
      await humanType(page, page.locator('#password-input'), adminCredentials.password);
      await humanClick(page, page.getByRole('button', { name: /login/i }).first());
    }
    const consent = page.getByRole('button', { name: /allow|authori[sz]e|accept|continue|grant|^yes/i }).first();
    if (await consent.isVisible({ timeout: 6_000 }).catch(() => false)) {
      await humanClick(page, consent);
    }
    callbackReq = await callbackRequest;
  }
  return callbackReq?.url() ?? '';
}
