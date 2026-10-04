import { expect, type Page } from '@playwright/test';
import { beat, humanClick } from 'wayfinder-demo-recording-kit';
import { demoRun } from './demo-config.js';

async function openBlueprintInEditor(page: Page): Promise<void> {
  await page.goto('/umbraco/section/settings');
  await page.waitForTimeout(1_500);
  await humanClick(page, page.getByText('Blueprints', { exact: true }));
  await page.waitForTimeout(1_500);
  await humanClick(page, page.getByRole('link', { name: demoRun.newDisplayName }));

  // data-wayfinder-active-service-blueprint/-service-blueprint-loaded exist as string constants
  // in the bundle (grepped from source) but aren't actually written to the DOM as literal
  // attributes on this version — confirmed live via a real ARIA snapshot showing the editor
  // genuinely loaded (heading, toolbar, canvas, all real). The heading is a real, visible,
  // robust readiness signal instead.
  await expect(page.getByRole('heading', { name: demoRun.newDisplayName, level: 1 })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole('application', { name: /graph canvas/i })).toBeVisible({ timeout: 30_000 });
}

/**
 * Translate Wayfinder's implementation back to the domain requirement it satisfies — never the other
 * way round. Deliberately generic rather than matching specific stage names/wording: the brief never
 * told the agent what to call anything, so this finds whichever stage the agent actually made into
 * the branch point (2+ outgoing routes = a real decision, not just a straight-through hop) rather
 * than assuming a name. Best-effort — if the agent's own design shape doesn't match this
 * expectation for some reason, skip gracefully rather than fail the whole act over a narration
 * flourish.
 */
async function pointOutTheDecision(page: Page): Promise<void> {
  const canvas = page.getByRole('application', { name: /graph canvas/i });
  const stageNodes = canvas.getByRole('button', { name: /Applicant queue|Caseworker queue/ });
  const stageCount = await stageNodes.count();
  for (let i = 0; i < stageCount; i++) {
    await humanClick(page, stageNodes.nth(i));
    await page.waitForTimeout(500);
    const routes = page.getByRole('region', { name: 'Outgoing routes' }).getByRole('article');
    if ((await routes.count()) >= 2) {
      await beat(
        page,
        'note',
        'The designer said only jugglers from a recognised authority can transfer. Here that ' +
          'rule is a decision point: each route out of this step carries its own condition, ' +
          'checked before the applicant goes any further.',
        { position: 'top' }
      );
      await page.waitForTimeout(1_000);
      return;
    }
  }
}

/** Act 4: open the agent's blueprint in the visual editor and tie what it drew back to the brief. */
export async function showBlueprintInEditor(page: Page): Promise<void> {
  await beat(page, 'intent', 'Same blueprint, opened in the visual editor a service designer would use. Let us see what the words became.');
  await openBlueprintInEditor(page);

  await beat(page, 'setup', 'Every stage, every decision, and every route the agent wrote.');
  await humanClick(page, page.getByRole('button', { name: 'Fit to screen' }));
  await page.waitForTimeout(600);

  await beat(
    page,
    'recap',
    'The eligibility decision, the document upload, and the review and declaration step: each ' + 'one traces back to a line in the brief.'
  );
  await pointOutTheDecision(page);

  await beat(
    page,
    'note',
    'Further round the graph, the document upload holds the evidence they asked to see, and the ' +
      'caseworker review holds the decision they said a person must always make.'
  );

  await humanClick(page, page.getByRole('tab', { name: /validation/i }));
  await page.waitForTimeout(800);
  await beat(
    page,
    'note',
    'The blueprint is valid and complete. This is the picture a team would sketch on a wall to ' +
      'agree how a service works. Here it is running.'
  );
}
