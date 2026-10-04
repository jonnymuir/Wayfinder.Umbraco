import { expect, type Locator, type Page } from '@playwright/test';
import { beat, humanClick } from 'wayfinder-demo-recording-kit';
import { demoRun } from './demo-config.js';
import { fillVisibleFields } from './form-filler.js';

/** The default 5-minute config timeout isn't enough for a multi-step generic walk (up to 9 applicant stages, each with real human-paced typing/clicks), plus the caseworker picking it up and deciding it, plus the applicant checking back for the outcome. */
export const ACT5_TIMEOUT_MS = 14 * 60_000;

const settle = (page: Page, timeout = 15_000) => page.waitForLoadState('networkidle', { timeout }).catch(() => {});

/**
 * Everything is scoped to #main-content — confirmed live this is load-bearing, not just tidy: an
 * unscoped 'form button' selector matches the page shell's own Sign Out form (which precedes main
 * content in the DOM, see ReferenceAppPageShell.cs), silently logging the applicant out mid-walk and
 * stalling the rest of the act for its entire budget waiting on a "Sign out" button that no longer
 * exists once already signed out.
 */
const mainContent = (page: Page) => page.locator('#main-content');

async function signOut(page: Page): Promise<void> {
  await humanClick(
    page,
    page
      .getByRole('button', { name: 'Sign out', exact: true })
      .or(page.locator('button', { hasText: 'Sign out' }))
      .first()
  );
  await page.waitForTimeout(500);
}

async function signInAs(page: Page, person: RegExp): Promise<void> {
  await page.goto('/demo/login');
  await humanClick(page, page.getByRole('button', { name: person }));
  await settle(page);
}

/**
 * The agent's own stage order/labels/field keys aren't fixed ahead of time — walk the journey
 * generically: on each stage, upload a file if a file input is present, tick any checkboxes present
 * (eligibility questions, the final declaration), fill any remaining required text input with a
 * plausible value, then submit via whichever button the form actually has.
 */
async function walkApplicantJourney(page: Page, main: Locator): Promise<void> {
  let fileBeatShown = false;
  for (let stepGuard = 0; stepGuard < 9; stepGuard++) {
    await page.waitForTimeout(500);
    if (!fileBeatShown && (await main.locator('input[type="file"]').count()) > 0) {
      await beat(page, 'note', 'The two documents the designer asked for: the licence certificate and proof of identity.');
      fileBeatShown = true;
    }
    await fillVisibleFields(page, main);

    // A summary-list's own per-row "Change" button is also a <button> in a <form> and renders
    // before the real continue button — exclude it or the walk loops back a stage.
    const submit = main.locator('form button[type="submit"], form button').filter({ hasNotText: /change/i }).first();
    if ((await submit.count()) === 0) break;
    await humanClick(page, submit);
    await settle(page);
    // A short settle: checking main.locator('form').count() straight after networkidle can race
    // the post-navigation DOM and read 0 forms, ending the walk a stage early (headed only).
    await page.waitForTimeout(500);
    if ((await main.locator('form').count()) === 0) break;
  }
}

async function applicantApplies(page: Page, main: Locator): Promise<void> {
  await beat(page, 'setup', 'Now as an applicant.');
  await signInAs(page, /Alex Applicant/i);

  await beat(page, 'intent', 'We will walk the journey the agent designed, the way a member of the public would.');
  await humanClick(page, page.getByRole('link', { name: 'Apply', exact: true }));
  await settle(page);

  await walkApplicantJourney(page, main);
  await beat(page, 'recap', 'Submitted: eligibility, the licence and identity documents, a check of the answers, and the declaration.');
}

/** Opens the picked item's current stage form (the review): the picked row exposes a link carrying ?instanceId=, whose visible text is the stage's own name, so match it by the href. */
async function openPickedItem(page: Page, main: Locator): Promise<void> {
  await humanClick(page, page.getByRole('button', { name: /pick ?up/i }).first());
  await page.waitForLoadState('load', { timeout: 15_000 }).catch(() => {});
  await page.waitForTimeout(1_500);
  const openPicked = main.locator('a[href*="instanceId="]').first();
  if ((await openPicked.count()) > 0) {
    await humanClick(page, openPicked);
    await page.waitForLoadState('load', { timeout: 15_000 }).catch(() => {});
    await page.waitForTimeout(1_500);
  }
}

/** Takes the approve action if it is on screen; true if it did. Doesn't fill the review stage's optional "what more do you need?" boxes — that is the request-more-info path, not this one. */
async function approveIfOffered(page: Page, main: Locator, beatShown: { value: boolean }): Promise<boolean> {
  const approveBtn = main.getByRole('button', { name: /approve/i }).first();
  if ((await approveBtn.count()) === 0 || !(await approveBtn.isVisible().catch(() => false))) {
    return false;
  }

  if (!beatShown.value) {
    await beat(
      page,
      'intent',
      "Casey approves the transfer. The designer said this is always a person's call, never " + 'an automatic yes.',
      { position: 'top' }
    );
    beatShown.value = true;
  }
  await humanClick(page, approveBtn);
  await page.waitForLoadState('load', { timeout: 15_000 }).catch(() => {});
  await page.waitForTimeout(1_200);
  return true;
}

/** Works a follow-on stage that needs input before its primary action; false if there is no such stage. */
async function completeFollowOnStage(page: Page, main: Locator): Promise<boolean> {
  const primary = main
    .locator('form button[type="submit"], form button')
    .filter({ hasNotText: /change|reject|request|decline|refuse|send back|more evidence|more information|put back|back to worklist/i })
    .first();
  if ((await primary.count()) === 0) return false;

  await fillVisibleFields(page, main);
  await humanClick(page, primary);
  await page.waitForLoadState('load', { timeout: 15_000 }).catch(() => {});
  await page.waitForTimeout(1_200);
  return true;
}

/** Pick it up and work it through to a decision. The brief said a person always makes this call, so the demo has to actually make it, not just show that the request arrived. */
async function caseworkerDecides(page: Page, main: Locator): Promise<void> {
  await beat(page, 'setup', 'And now the caseworker who picks it up.');
  await signOut(page);
  await signInAs(page, /Casey Caseworker/i);

  await beat(page, 'intent', "This is the caseworker queue. The blueprint's own routing sent the request straight here.");
  await humanClick(page, page.getByRole('link', { name: 'Caseworker queue', exact: true }));
  await settle(page);
  await page.waitForTimeout(1_200);
  await expect(main.getByText(demoRun.newDisplayName).first()).toBeVisible({ timeout: 20_000 });

  await openPickedItem(page, main);
  await beat(page, 'setup', "Casey has the applicant's answers and both documents they sent. Now the decision.");

  const approveBeatShown = { value: false };
  for (let step = 0; step < 5; step++) {
    await page.waitForTimeout(500);
    if (await approveIfOffered(page, main, approveBeatShown)) continue;
    if (!(await completeFollowOnStage(page, main))) break;
  }

  // The approved request has moved to the applicant's queue, so lingering on its URL as the
  // caseworker shows an "access denied" panel — go back to the (now clear) worklist instead.
  await page.goto('/caseworker-queue');
  await settle(page);
  await beat(page, 'recap', 'Approved, and the request has left the queue.');
}

async function applicantChecksOutcome(page: Page, main: Locator): Promise<void> {
  await beat(page, 'setup', 'The applicant checks back.');
  await signOut(page);
  await signInAs(page, /Alex Applicant/i);
  await humanClick(page, page.getByRole('link', { name: 'Apply', exact: true }));
  await settle(page);
  await page.waitForTimeout(800);
  // Tight on purpose: if the caseworker walk didn't actually approve it, the applicant would still
  // see "awaiting a decision" here, and this take should fail rather than ship that.
  await expect(main).toContainText(/approv|granted|transferr?ed|on the register/i, { timeout: 15_000 });

  await beat(
    page,
    'recap',
    'The transfer is done: the service the designer described in plain language, published in ' +
      'Umbraco, and run from the first question to the final decision by real people.'
  );
}

/** Act 5: run the published service as the applicant, then the caseworker, then the applicant again. */
export async function runTheService(page: Page): Promise<void> {
  const main = mainContent(page);
  await applicantApplies(page, main);
  await caseworkerDecides(page, main);
  await applicantChecksOutcome(page, main);
}
