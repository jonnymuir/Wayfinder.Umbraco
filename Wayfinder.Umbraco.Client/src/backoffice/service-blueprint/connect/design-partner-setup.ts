// The text the "Connect a design partner" panel shows and copies, built from what the server says
// about this site (DesignPartnerController). Kept apart from the element so every snippet is
// plain data.

export type DesignPartnerClient = 'claude-code' | 'claude-desktop' | 'vscode';

export interface DesignPartnerConnection {
  serverName: string;
  serverUrl: string;
  clientId: string;
  callbackPort: number;
  development: boolean;
  redirectUris: string[];
  clients: { client: DesignPartnerClient; missingRedirectUris: string[]; ready: boolean }[];
}

export const WALKTHROUGH_URL =
  'https://github.com/jonnymuir/Wayfinder.Umbraco/blob/main/docs/mcp-authoring-walkthrough.md#act-1-connect-the-design-partner';

export const MCP_SETUP_URL =
  'https://github.com/jonnymuir/Wayfinder.Umbraco#turning-on-mcp-authoring-in-your-own-site';

export const claudeCodeAdd = (c: DesignPartnerConnection) =>
  `claude mcp add --transport http ${c.serverName} ${c.serverUrl} --client-id ${c.clientId} --callback-port ${c.callbackPort}`;

export const claudeCodeLogin = (c: DesignPartnerConnection) => `claude mcp login ${c.serverName}`;

/** A project-scoped `.mcp.json`, the same file `claude mcp add --scope project` writes. */
export const claudeCodeProjectFile = (c: DesignPartnerConnection) =>
  json({
    mcpServers: {
      [c.serverName]: { type: 'http', url: c.serverUrl, oauth: { clientId: c.clientId, callbackPort: c.callbackPort } },
    },
  });

/** `.vscode/mcp.json`. VS Code asks for the client ID the first time it signs in. */
export const vsCodeFile = (c: DesignPartnerConnection) =>
  json({ servers: { [c.serverName]: { type: 'http', url: c.serverUrl } } });

export const redirectUrisSetting = (uris: string[]) => json({ Wayfinder: { Mcp: { RedirectUris: uris } } });

/** True when Claude Code would meet this site's self-signed development certificate. */
export const usesLocalCertificate = (c: DesignPartnerConnection) => {
  const url = new URL(c.serverUrl);
  return c.development && url.protocol === 'https:' && ['localhost', '127.0.0.1'].includes(url.hostname);
};

export const newServicePrompt = () =>
  [
    "I'd like to design a new service with you, using the Wayfinder tools.",
    '',
    'The problem: [who it is for, what they need to do, and what goes wrong today].',
    'What I know about how it needs to work: [the rules, the evidence we need, who decides].',
    '',
    'Ask me anything you need, in plain language. Validate the blueprint, then save it.',
  ].join('\n');

export const notesPrompt = (displayName: string, definitionKey: string) =>
  [
    `Work through my open design notes on the "${displayName}" blueprint (${definitionKey}).`,
    '',
    'For each note, change the blueprint, save it, and resolve the note with a sentence on what you did.',
    'If a note needs a decision from me, reply to it with your question instead of guessing.',
  ].join('\n');

const json = (value: unknown) => `${JSON.stringify(value, null, 2)}\n`;
