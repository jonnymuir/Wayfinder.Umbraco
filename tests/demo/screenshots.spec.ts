import { test, request as apiRequest, type APIRequestContext, type Page } from '@playwright/test';
import { mkdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Produces the still images the repo README and docs embed. Repeatable in ~30s against a running
// Wayfinder.Umbraco.ReferenceApp — it seeds the "transfer a juggling licence" blueprint directly
// via the REST authoring API (the same endpoint save_service_blueprint calls), then captures each
// surface a reader of the README wants to see. Not the ~15-minute agent recording
// (mcp-authoring-demo.spec.ts) — this one never touches Claude.
//
// Run:  npm run demo:screenshots   (from tests/demo/, reference app already up — see README.md)
// Out:  assets/screenshots/*.png   (repo root — commit the keepers)

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const outDir = path.join(__dirname, '..', '..', 'assets', 'screenshots');
mkdirSync(outDir, { recursive: true });

const admin = { email: 'admin@example.test', password: 'Wayfinder123!' };
// Matches ReferenceMcpDemoAgentSeeder — seeded on every reference-app startup, idempotently.
const agent = { clientId: 'wayfinder-demo-agent', clientSecret: 'DemoAgentLocal!12345' };

const fixture = JSON.parse(
  readFileSync(path.join(__dirname, 'support', 'rehearsal-fake-blueprint.json'), 'utf8')
);
const blueprintKey: string = fixture.definitionKey;
const displayName: string = fixture.displayName;

async function mintToken(api: APIRequestContext): Promise<string> {
  for (let i = 0; i < 10; i++) {
    const resp = await api.post('/umbraco/management/api/v1/security/back-office/token', {
      form: {
        grant_type: 'client_credentials',
        client_id: `umbraco-back-office-${agent.clientId}`,
        client_secret: agent.clientSecret
      }
    });
    if (resp.ok()) return (await resp.json()).access_token;
    await new Promise(r => setTimeout(r, 1_000));
  }
  throw new Error('could not mint a client-credentials token to seed the screenshot blueprint');
}

async function loginBackoffice(page: Page): Promise<void> {
  await page.goto('/umbraco/login');
  await page.getByLabel(/email/i).fill(admin.email);
  await page.locator('#password-input').fill(admin.password);
  await page.locator('button[type="submit"]').first().click();
  await page.waitForURL(url => !url.pathname.includes('/login'), { timeout: 30_000 });
  await page.waitForTimeout(1_000);
}

async function openBlueprintInEditor(page: Page): Promise<void> {
  await page.goto('/umbraco/section/settings');
  await page.waitForTimeout(1_500);
  await page.getByText('Blueprints', { exact: true }).click();
  await page.waitForTimeout(1_000);
  await page.getByRole('link', { name: displayName }).click();
  await page.getByRole('heading', { name: displayName, level: 1 }).waitFor({ timeout: 30_000 });
  await page.getByRole('application', { name: /graph canvas/i }).waitFor({ timeout: 30_000 });
}

test.describe.serial('Wayfinder.Umbraco README screenshots', () => {
  let api: APIRequestContext;
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    api = await apiRequest.newContext({ baseURL: 'https://localhost:44399', ignoreHTTPSErrors: true });
    const token = await mintToken(api);
    const put = await api.put(
      `/umbraco/management/api/v1/wayfinder/service-blueprints/${blueprintKey}`,
      { headers: { Authorization: `Bearer ${token}` }, data: fixture }
    );
    if (!put.ok()) throw new Error(`seeding the blueprint failed (${put.status()}): ${await put.text()}`);

    const context = await browser.newContext({
      viewport: { width: 1600, height: 1000 },
      ignoreHTTPSErrors: true,
      deviceScaleFactor: 2
    });
    page = await context.newPage();
    await loginBackoffice(page);

    // Best-effort: point /apply at the seeded blueprint so the applicant-journey shot shows this
    // service rather than the reference app's placeholder. A change here that doesn't take just
    // means that one screenshot shows the default — not a run failure.
    try {
      await page.goto('/umbraco/section/content');
      await page.waitForTimeout(1_500);
      await page.getByText('Apply', { exact: true }).first().click();
      await page.waitForTimeout(1_500);
      await page.locator('umb-ref-grid-block').first().click();
      await page.waitForTimeout(1_000);
      const keyField = page.getByLabel('Blueprint key');
      if ((await keyField.inputValue()) !== blueprintKey) {
        await keyField.fill(blueprintKey);
        await page.getByRole('button', { name: 'Update', exact: true }).click();
        await page.waitForTimeout(500);
        await page.getByRole('button', { name: 'Save and publish', exact: true }).click();
        await page.getByText(/published/i).first().waitFor({ timeout: 15_000 });
      }
    } catch (err) {
      console.warn(`Could not repoint /apply at the seeded blueprint: ${err instanceof Error ? err.message : String(err)}`);
    }
  });

  test.afterAll(async () => {
    await page?.close();
    await api?.dispose();
  });

  test('blueprints list', async () => {
    await page.goto('/umbraco/section/settings');
    await page.waitForTimeout(1_500);
    await page.getByText('Blueprints', { exact: true }).click();
    await page.getByRole('link', { name: displayName }).waitFor({ timeout: 20_000 });
    await page.waitForTimeout(500);
    await page.screenshot({ path: path.join(outDir, 'blueprints-list.png') });
  });

  test('visual editor graph', async () => {
    await openBlueprintInEditor(page);
    await page.getByRole('button', { name: 'Fit to screen' }).click();
    await page.waitForTimeout(1_000);
    await page.screenshot({ path: path.join(outDir, 'visual-editor-graph.png') });
  });

  test('visual editor decision point', async () => {
    await openBlueprintInEditor(page);
    await page.getByRole('button', { name: 'Fit to screen' }).click();
    await page.waitForTimeout(800);
    try {
      const canvas = page.getByRole('application', { name: /graph canvas/i });
      await canvas.getByRole('button', { name: new RegExp(fixture.stages[0].displayName, 'i') }).click();
      await page.waitForTimeout(800);
      await page.screenshot({ path: path.join(outDir, 'visual-editor-decision-point.png') });
    } catch (err) {
      console.warn(`Skipped the decision-point shot: ${err instanceof Error ? err.message : String(err)}`);
    }
  });

  test('applicant journey', async () => {
    await page.goto('/demo/login');
    await page.getByRole('button', { name: /Alex Applicant/i }).click();
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    await page.getByRole('link', { name: 'Apply', exact: true }).click();
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    await page.locator('#main-content').waitFor({ timeout: 15_000 });
    await page.waitForTimeout(800);
    await page.screenshot({ path: path.join(outDir, 'applicant-journey.png') });
  });

  test('caseworker worklist', async () => {
    await page.goto('/demo/login');
    await page.getByRole('button', { name: /Casey Caseworker/i }).click();
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    await page.getByRole('link', { name: 'Caseworker queue', exact: true }).click();
    await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
    await page.locator('#main-content').waitFor({ timeout: 15_000 });
    await page.waitForTimeout(800);
    await page.screenshot({ path: path.join(outDir, 'caseworker-worklist.png') });
  });
});
