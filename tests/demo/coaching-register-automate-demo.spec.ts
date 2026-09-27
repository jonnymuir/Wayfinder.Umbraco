import { test, expect, type Page } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  beat, showSlate, clearSlate, moveNarrationTo, startNarrationTimeline, getNarrationTimeline,
  humanClick, humanType
} from 'wayfinder-demo-recording-kit';

// One shared Page across every act (see narrated-single-take-demo-recording skill). Playwright
// records one video per Page, so as long as nothing ever opens a second page, later acts are just
// later timestamps in the same file, not separate clips needing to be stitched together.
const __dirname = path.dirname(fileURLToPath(import.meta.url));
const footageDir = path.join(__dirname, 'demo-footage');
mkdirSync(footageDir, { recursive: true });

function tryConvertToMp4(webmPath: string): void {
  const mp4Path = webmPath.replace(/\.webm$/, '.mp4');
  try {
    execFileSync(
      'ffmpeg',
      ['-y', '-i', webmPath, '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-c:a', 'aac', mp4Path],
      { stdio: 'ignore' }
    );
    console.log(`Also wrote ${mp4Path}.`);
  } catch {
    console.log('ffmpeg not found on PATH, skipping the .mp4 convenience copy. The .webm is the real output.');
  }
}

// Not a CI test, a demo-recording tool. Run with `npm run demo:record:automate` from tests/demo
// (see README.md for the full operator setup: warm the reference app and Mailpit via Aspire
// first, off-camera). Mirrors docs/automate-support-system-walkthrough.md's "Run the journey"
// section, and Beats B-D of docs/demos/service-design-meetup-talk.md (Umbraco.Prism). Beat A (the
// Program.cs code reveal) has no recordable equivalent, since screen-recording an editor is out
// of scope for a browser-driven Playwright take.

const adminCredentials = { email: 'admin@example.test', password: 'Wayfinder123!' };
const mailpitOrigin = 'https://localhost:8025';

test.describe.serial('Wayfinder.Umbraco coaching-register + Automate demo', () => {
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    const recordingSize = { width: 1920, height: 1080 };
    const context = await browser.newContext({
      viewport: recordingSize,
      recordVideo: { dir: footageDir, size: recordingSize },
      ignoreHTTPSErrors: true
    });
    page = await context.newPage();
    startNarrationTimeline();
  });

  test.afterAll(async () => {
    const video = page?.video();
    await page?.close();
    if (video) {
      const finalPath = path.join(footageDir, 'coaching-register-automate-demo.webm');
      await video.saveAs(finalPath);
      await video.delete();
      tryConvertToMp4(finalPath);
      writeFileSync(
        path.join(footageDir, 'coaching-register-automate-narration-timeline.json'),
        JSON.stringify(getNarrationTimeline(), null, 2)
      );
    }
  });

  test('Cold open, introduce the demo', async () => {
    await showSlate(page, {
      eyebrow: 'WAYFINDER FOR UMBRACO',
      title: 'A real support process, wired with config, not code',
      body:
        'A coach applies to join the National Juggling Federation coaching register. An NJF ' +
        'registrar reviews it, then runs a coaching-standards check against an external body. ' +
        "That check is a real Umbraco Automate automation, seeded and published in code, but " +
        "requiring no bespoke integration code of its own. We'll watch it branch, email a " +
        'standards officer, wait for a human decision, and resolve back to a real applicant.',
      holdMs: 13_000
    });
    await clearSlate(page);
  });

  test('Act 1, the blueprint in the backoffice', async () => {
    await beat(page, 'setup', "This is an ordinary Umbraco 17 backoffice, nothing bespoke in the tree beyond Settings.");
    await page.goto('/umbraco/login');
    await humanType(page, page.getByLabel(/email/i), adminCredentials.email);
    await humanType(page, page.locator('#password-input'), adminCredentials.password);
    await humanClick(page, page.locator('button[type="submit"]').first());
    await page.waitForURL(url => !url.pathname.includes('/login'), { timeout: 30_000 });
    await page.waitForTimeout(1_000);

    await beat(
      page,
      'intent',
      'Settings holds a "Blueprints" entry, the same placement convention as Umbraco\'s own ' +
        "Webhooks package. This is the service design behind what we're about to run."
    );
    await page.goto('/umbraco/section/settings');
    await page.waitForTimeout(1_200);
    await humanClick(page, page.getByText('Blueprints', { exact: true }));
    await page.waitForTimeout(1_200);
    await humanClick(page, page.getByRole('link', { name: 'Register as a juggling coach' }));
    await page.waitForTimeout(1_500);

    await beat(page, 'note', 'Coach applies, an NJF registrar reviews it, a coaching-standards check runs, then a decision comes back.');
    await humanClick(page, page.getByRole('button', { name: 'Fit to screen' }));
    await page.waitForTimeout(600);

    await beat(
      page,
      'recap',
      "Three lanes: the coach, the registrar, and the coaching-standards check itself, the " +
        "support process most service blueprints leave out. Let's run it for real.",
      { position: 'top' }
    );
  });

  test('Act 2, apply as the coach', async () => {
    await beat(page, 'setup', 'First, the coach.', { position: 'top' });
    await page.goto('/demo/login');
    await humanClick(page, page.getByRole('button', { name: /Alex Applicant/i }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    await beat(page, 'intent', "We'll apply to join the coaching register, with fewer than two years' experience, so this lands on a human, not the auto-accredit path.");
    // action=start-new: njf-coaching-register is requestPolicy "single" with allowManualRestart.
    // Without this, a rerun against a DB that already has a completed instance for this persona
    // lands straight on the old outcome instead of a fresh application form.
    await page.goto('/apply-to-coach?action=start-new');
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    await humanType(page, page.getByLabel('Full name'), 'Alex Applicant');
    await humanType(page, page.getByLabel('Email address'), 'alex.applicant@example.com');
    await humanType(page, page.getByLabel('Years of coaching experience'), '1');
    await humanType(page, page.getByLabel('Safeguarding disclosure reference'), 'SG-2026-0417');
    await humanType(page, page.locator('#firstAidExpiry-day, input[name$="-day"]').first(), '1');
    await humanType(page, page.locator('#firstAidExpiry-month, input[name$="-month"]').first(), '6');
    await humanType(page, page.locator('#firstAidExpiry-year, input[name$="-year"]').first(), '2030');

    await humanClick(page, page.getByRole('button', { name: 'Submit application' }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    await expect(page.getByText(/waiting for: registrar/i)).toBeVisible({ timeout: 15_000 });

    await beat(
      page,
      'recap',
      "That's a real, live wait state, nothing polling client-side and hoping. The application " +
        "is genuinely paused, waiting for the registrar."
    );
  });

  test('Act 3, review, the standards check, and the Automate approval', async () => {
    // The default 5-minute config timeout isn't enough here: two persona switches, a real
    // Automate run and approval dialog, a Mailpit navigation, and around a dozen narration beats
    // each holding for a reading-paced duration, all with human-paced clicks and typing.
    test.setTimeout(10 * 60_000);

    await beat(page, 'setup', 'Now the registrar.', { position: 'top' });
    await humanClick(page, page.getByRole('button', { name: 'Sign out', exact: true }));
    await page.waitForTimeout(500);
    await page.goto('/demo/login');
    await humanClick(page, page.getByRole('button', { name: /Casey Caseworker/i }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    await beat(page, 'intent', "The blueprint's own routing already put it here, the coaching register queue.");
    await page.goto('/coaching-register-queue');
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    // The row's own link is labelled "View" while unassigned and only becomes "Review" once
    // picked up, so pickup has to happen first. Matches the real queue mechanics confirmed live:
    // "Pick up" reassigns the row in place and stays on this same list, it does not navigate.
    await humanClick(page, page.getByRole('button', { name: /pick ?up/i }).first());
    await page.waitForLoadState('load', { timeout: 15_000 }).catch(() => {});
    await page.waitForTimeout(800);
    await humanClick(page, page.getByRole('link', { name: /review|view/i }).first());
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    await beat(page, 'note', "Casey has the applicant's answers. Now runs the coaching-standards check.");
    await humanClick(page, page.getByRole('button', { name: 'Run coaching-standards check' }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    await expect(page.getByText(/waiting for: standards/i)).toBeVisible({ timeout: 15_000 });

    await beat(
      page,
      'intent',
      "That's the third lane of a service blueprint that's easy to forget about, the support " +
        'process. This just POSTed a real, signed webhook out to an Umbraco Automate automation. ' +
        "Let's go look at it."
    );

    // Backoffice is already an authenticated session on this same page (cookies persist), so no
    // re-login is needed, just navigate to the Automate section.
    await page.goto('/umbraco/section/automate');
    await page.waitForTimeout(1_500);
    await beat(page, 'note', "Published, one run in progress, the automation this instance just triggered.");

    await humanClick(page, page.getByRole('tab', { name: 'Approvals' }));
    await page.waitForTimeout(1_000);
    await expect(page.getByRole('row', { name: /NJF Coaching Standards/ })).toBeVisible({ timeout: 15_000 });

    await beat(
      page,
      'intent',
      "This automation branches on years of experience, emails a standards officer, and waits " +
        "for exactly this: a real human accredited, provisional or referred decision."
    );

    // Mailpit: a real inbox, not a mock. The email genuinely arrived. .first() is deliberate,
    // not just defensive: a rerun against an inbox nobody cleared between takes can have more
    // than one matching subject line, and this beat only needs to show that one is there.
    await page.goto(mailpitOrigin);
    await page.waitForTimeout(1_200);
    await expect(page.getByText(/coaching register application needs review/i).first()).toBeVisible({ timeout: 15_000 });
    await beat(page, 'recap', "The standards officer's email, genuinely delivered.");

    // Back to the Automate section to make the decision.
    await page.goto('/umbraco/section/automate/dashboard/approvals');
    await page.waitForTimeout(1_200);
    await beat(page, 'intent', 'Approving as provisional, a mentored session required within six months.');
    await humanClick(
      page,
      page.getByRole('row', { name: /NJF Coaching Standards/ }).getByRole('button', { name: 'Approve' })
    );
    await page.waitForTimeout(500);
    // Not page.getByRole('dialog'): that role matches an unrelated, buttonless element elsewhere
    // in this backoffice, confirmed live. The real approval modal is this custom element, rendered
    // in a portal container rather than nested under anything ARIA reports as role="dialog".
    const approvalModal = page.locator('ua-approval-decision-modal');
    await humanClick(page, approvalModal.getByRole('button', { name: 'Approve', exact: true }));
    await page.waitForTimeout(1_500);

    await beat(
      page,
      'recap',
      "A real human decision, recorded, and the registrar's own wait screen just released on " +
        'the back of it.',
      { position: 'top' }
    );

    await page.goto('/coaching-register-queue');
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    await humanClick(page, page.getByRole('link', { name: 'Review', exact: true }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    await expect(page.getByRole('heading', { name: 'Confirm the outcome' })).toBeVisible({ timeout: 15_000 });

    await beat(page, 'note', "Team-tray ownership survived the whole send-and-wait round trip. Still Casey's case.");
    await humanClick(page, page.getByRole('button', { name: 'Record and notify the applicant' }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});

    await beat(page, 'setup', 'And the applicant, checking back.', { position: 'top' });
    await humanClick(page, page.getByRole('button', { name: 'Sign out', exact: true }));
    await page.waitForTimeout(500);
    await page.goto('/demo/login');
    await humanClick(page, page.getByRole('button', { name: /Alex Applicant/i }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    // A clean navigation from the home page (rather than reusing an old query-string URL) avoids a
    // stale queue-scoped access check firing alongside the real content on first render.
    await page.goto('/');
    await humanClick(page, page.getByRole('link', { name: 'Apply to coach (as a citizen)' }));
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    await expect(page.getByRole('heading', { name: /coaching register application is complete/i })).toBeVisible({ timeout: 15_000 });

    await beat(
      page,
      'recap',
      'Branching logic, a real external system, a human approval gate, and a decision delivered ' +
        'back to a real applicant. The only code anyone wrote for the integration itself was ' +
        'three delegates and a webhook callback route.'
    );
  });

  test('Closing slate', async () => {
    await showSlate(page, {
      eyebrow: 'WAYFINDER FOR UMBRACO',
      title: 'Support processes are a first-class lane, not an afterthought',
      body:
        'A branching decision, a real external system, a human approval gate, a push notification ' +
        "back to the applicant, configured, not coded. That's the third lane of a service " +
        'blueprint most tools leave out entirely.'
    });
  });
});
