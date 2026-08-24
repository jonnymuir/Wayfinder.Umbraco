// Checks that Wayfinder.Umbraco.ReferenceApp is already warmed and running before burning a
// recording take on a stack that isn't ready. Ported/adapted from Umbraco.Prism's
// tests/demo/support/demo-prereqs-setup.ts — that app orchestrates Keycloak + a second service,
// this one is fully self-contained (single project, demo cookie auth, local SQLite), so there's
// only one port to check.
import { execSync } from 'node:child_process';

function listListeningPids(port: number): string[] {
  try {
    const output = execSync(`lsof -t -iTCP:${port} -sTCP:LISTEN`, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
    return output.trim().split(/\s+/).filter(v => /^\d+$/.test(v));
  } catch {
    return [];
  }
}

export default async function globalSetup() {
  const port = 44399; // Wayfinder.Umbraco.ReferenceApp's real HTTPS profile (launchSettings.json).
  if (listListeningPids(port).length === 0) {
    throw new Error(
      `Demo recording requires Wayfinder.Umbraco.ReferenceApp to already be running on ` +
        `https://localhost:${port}. Start it first:\n` +
        `  dotnet run --project Wayfinder.Umbraco.ReferenceApp --launch-profile Wayfinder.Umbraco.ReferenceApp\n` +
        `See tests/demo/README.md.`
    );
  }
}
