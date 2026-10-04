import { expect, type Page } from '@playwright/test';
import { beat, humanClick, humanType } from 'wayfinder-demo-recording-kit';
import { demoRun, seededDefinitionKey } from './demo-config.js';

/** Act 3: point the Apply page's blueprint block at the agent's blueprint and publish it. */
export async function publishToSite(page: Page): Promise<void> {
  await beat(page, 'intent', 'The blueprint is content in Umbraco now. Let us put it on the site.');

  await page.goto('/umbraco/section/content');
  await page.waitForTimeout(1_500);
  await humanClick(page, page.getByText('Apply', { exact: true }).first());
  await page.waitForTimeout(1_500);

  await beat(page, 'setup', 'This block renders the Apply page. Today it points at a placeholder service.');
  await humanClick(page, page.locator('umb-ref-grid-block').first());
  await page.waitForTimeout(1_000);

  const keyField = page.getByLabel('Blueprint key');
  await expect(keyField).toHaveValue(seededDefinitionKey, { timeout: 10_000 });
  await humanType(page, keyField, demoRun.newDefinitionKey);
  await humanClick(page, page.getByRole('button', { name: 'Update', exact: true }));
  await page.waitForTimeout(500);

  await humanClick(page, page.getByRole('button', { name: 'Save and publish', exact: true }));
  await expect(page.getByText(/published/i).first()).toBeVisible({ timeout: 15_000 });

  await beat(page, 'recap', 'One field, one publish. Apply now serves the service the designer described.');
}
