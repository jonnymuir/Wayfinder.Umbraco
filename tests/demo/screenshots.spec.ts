import { test, request as apiRequest, type APIRequestContext, type Page } from '@playwright/test';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
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

// A minimal but genuinely valid one-page PDF, for the file-upload stage of the applicant walk
// that populates the caseworker queue (same literal the recording spec uses).
const MINIMAL_PDF = Buffer.from(
  '%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n' +
    '2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n' +
    '3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\n' +
    'trailer<</Root 1 0 R>>\n%%EOF',
  'utf8'
);
const evidencePdfPath = path.join(tmpdir(), 'wayfinder-umbraco-screenshot-evidence.pdf');
writeFileSync(evidencePdfPath, MINIMAL_PDF);

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
    const auth = { Authorization: `Bearer ${token}` };

    // The authoring API is optimistic-concurrency: a PUT carries the version it was loaded at.
    // On a fresh database the blueprint doesn't exist and version 0 is right; on a re-run it's
    // already there at a higher version, so read the current one and PUT against that.
    const existing = await api
      .get(`/umbraco/management/api/v1/wayfinder/service-blueprints/${blueprintKey}`, { headers: auth })
      .catch(() => null);
    if (existing?.ok()) {
      const body = await existing.json();
      fixture.version = typeof body.version === 'number' ? body.version : fixture.version;
    }
    const put = await api.put(
      `/umbraco/management/api/v1/wayfinder/service-blueprints/${blueprintKey}`,
      { headers: auth, data: fixture }
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
      // Anchor at the start so this matches the stage node ("Do you already hold a licence?,
      // Applicant queue") and not the transition edge chips ("Transition submit, Do you already
      // hold a licence? to ...").
      const stageName = String(fixture.stages[0].displayName).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      const canvas = page.getByRole('application', { name: /graph canvas/i });
      await canvas.getByRole('button', { name: new RegExp('^' + stageName, 'i') }).click();
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

  test('submit an applicant request', async () => {
    // Walk Alex through the journey so the caseworker queue has a real row to show. Generic on
    // purpose (tick every checkbox, fill every file input, submit) so it survives small changes to
    // the seeded fixture. Best-effort: a warning, not a failure, if a stage shape defeats it.
    try {
      await page.goto('/demo/login');
      await page.getByRole('button', { name: /Alex Applicant/i }).click();
      await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
      await page.getByRole('link', { name: 'Apply', exact: true }).click();
      await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
      const main = page.locator('#main-content');
      for (let step = 0; step < 6; step++) {
        await page.waitForTimeout(400);
        const files = main.locator('input[type="file"]');
        for (let f = 0; f < (await files.count()); f++) await files.nth(f).setInputFiles(evidencePdfPath);
        const boxes = main.locator('input[type="checkbox"]');
        for (let b = 0; b < (await boxes.count()); b++) {
          const cb = boxes.nth(b);
          if (!(await cb.isChecked())) await cb.check();
        }
        const submit = main
          .locator('form button[type="submit"], form button')
          .filter({ hasNotText: /change/i })
          .first();
        if ((await submit.count()) === 0) break;
        await submit.click();
        await page.waitForLoadState('networkidle', { timeout: 15_000 }).catch(() => {});
        await page.waitForTimeout(400);
        if ((await main.locator('form').count()) === 0) break;
      }
      await page.getByRole('button', { name: 'Sign out', exact: true }).click().catch(() => {});
    } catch (err) {
      console.warn(`Could not complete the applicant walk: ${err instanceof Error ? err.message : String(err)}`);
    }
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
