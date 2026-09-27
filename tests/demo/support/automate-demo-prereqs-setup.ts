// Checks that both Wayfinder.Umbraco.ReferenceApp AND Mailpit are already running before burning
// a recording take on a stack that isn't ready, this demo (unlike mcp-authoring-demo.spec.ts)
// actively depends on Mailpit for the standards-officer email beat, so only the Aspire-orchestrated
// boot (Wayfinder.Umbraco.AppHost) satisfies it, not a bare `dotnet run` of the reference app alone.
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
  const referenceAppPort = 44399;
  const mailpitPort = 8025;
  const missing: string[] = [];
  if (listListeningPids(referenceAppPort).length === 0) missing.push(`Wayfinder.Umbraco.ReferenceApp (https://localhost:${referenceAppPort})`);
  if (listListeningPids(mailpitPort).length === 0) missing.push(`Mailpit (https://localhost:${mailpitPort})`);

  if (missing.length > 0) {
    throw new Error(
      `Demo recording requires both the reference app and Mailpit already running (this demo's ` +
        `Automate act needs Mailpit, the plain launch-profile command doesn't start it). Missing: ` +
        `${missing.join(', ')}. Start the full stack via Aspire first:\n` +
        `  dotnet run --project ../../Wayfinder.Umbraco.AppHost\n` +
        `See tests/demo/README.md.`
    );
  }
}
