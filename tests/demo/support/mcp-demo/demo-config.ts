import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// One continuous take across every act, sharing a single Page created in beforeAll — Playwright
// records one video per Page, so as long as nothing ever opens a second page, "Act 5" is just a
// later timestamp in the same file as "Act 1", not a separate clip to stitch afterward. Ported
// structure from Umbraco.Prism's garden-waste-demo.spec.ts / tests/demo/README.md.
//
// Not a CI test — a demo-recording tool. Run with `npm run demo:record` from tests/demo (see
// README.md for the full operator setup: warm the reference app first, off-camera).

const here = path.dirname(fileURLToPath(import.meta.url));
export const demoDir = path.join(here, '..', '..');
export const footageDir = path.join(demoDir, 'demo-footage');
mkdirSync(footageDir, { recursive: true });

export const adminCredentials = { email: 'admin@example.test', password: 'Wayfinder123!' };
// Must match ReferenceMcpDemoAgentSeeder.ClientId/ClientSecret exactly — that seeder provisions
// the real user + credentials these constants exchange for a token; it has no env-var override
// of its own, so this can't safely diverge from it via environment either.
export const mcpAgentClientId = 'wayfinder-demo-agent';
export const mcpAgentClientSecret = 'DemoAgentLocal!12345';
export const seededDefinitionKey = 'reference-demo';
export const mcpUrl = 'https://localhost:44399/wayfinder/service-blueprint-authoring/mcp';

/**
 * What Act 2 discovers and Acts 3 and 4 read. Deliberately NOT hardcoded — the brief (see Act 2)
 * never tells the agent what definitionKey or exact displayName to use, on purpose: a real service
 * designer wouldn't dictate an internal implementation slug. Act 2 finds whatever the agent
 * actually chose (the one new entry that appears in the blueprint list beyond the seeded one).
 */
export const demoRun = { newDefinitionKey: '', newDisplayName: '' };

// Deliberately OUTSIDE this repo checkout — the whole point of Act 1/2 is proving the agent has
// no filesystem access to the codebase, only the MCP tools it was just given (--tools below
// enforces that regardless of cwd, but a scratch directory with no repo in reach keeps the
// framing honest, same convention as Umbraco.Prism's own tests/demo/README.md Act 4 setup).
// mkdtemp, not a fixed name under the shared temp directory: it creates a fresh directory only
// this user can read, so nothing else on the machine can pre-create or symlink the paths below.
export const scratchDir = mkdtempSync(path.join(tmpdir(), 'wayfinder-umbraco-demo-scratch-'));
export const claudeSessionLogPath = path.join(scratchDir, 'claude-session.log');

// Minimal but genuinely valid one-page PDFs — small enough to inline as literals, real enough
// that a browser file-upload input and a server-side content-type check both accept them as real
// PDFs. Two distinct files (different names AND bytes) so the applicant's licence certificate and
// their proof of identity don't read as "the same file uploaded twice" on camera.
function makePdf(caption: string): Buffer {
  return Buffer.from(
    '%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n' +
      '2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n' +
      '3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 200]>>endobj\n' +
      `% ${caption}\ntrailer<</Root 1 0 R>>\n%%EOF`,
    'utf8'
  );
}
export const licenceCertificatePath = path.join(scratchDir, 'juggling-licence-certificate.pdf');
export const proofOfIdentityPath = path.join(scratchDir, 'proof-of-identity.pdf');
writeFileSync(licenceCertificatePath, makePdf('Current juggling licence certificate'));
writeFileSync(proofOfIdentityPath, makePdf('Passport photo page'));
