// "Connect a design partner": setup for each MCP client with this site's own server URL, client
// ID and callback port filled in, plus starter prompts. Everything shown comes from
// GET /wayfinder/design-partner, so the panel never offers a setup the site would refuse.

import { LitElement, css, html, nothing, type TemplateResult } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';
import { serviceBlueprintFetch, wayfinderFetch } from '../service-blueprint-http.js';
import {
  MCP_SETUP_URL,
  WALKTHROUGH_URL,
  claudeCodeAdd,
  claudeCodeLogin,
  claudeCodeProjectFile,
  copilotCliFile,
  newServicePrompt,
  notesPrompt,
  redirectUrisSetting,
  usesLocalCertificate,
  vsCodeFile,
  type DesignPartnerClient,
  type DesignPartnerConnection,
} from './design-partner-setup.js';

type Blueprint = { definitionKey: string; displayName: string };

const CLIENTS: { id: DesignPartnerClient | 'other'; label: string }[] = [
  { id: 'claude-code', label: 'Claude Code' },
  { id: 'claude-desktop', label: 'Claude Desktop' },
  { id: 'vscode', label: 'VS Code' },
  { id: 'github-copilot', label: 'GitHub Copilot' },
  { id: 'other', label: 'Other MCP client' },
];

@customElement('wayfinder-connect-design-partner-modal')
export class WayfinderConnectDesignPartnerModalElement extends UmbElementMixin(LitElement) {
  modalContext?: { submit: () => void; reject: () => void };

  @state() private _connection?: DesignPartnerConnection | null;
  @state() private _error: string | null = null;
  @state() private _client: DesignPartnerClient | 'other' = 'claude-code';
  @state() private _blueprints: Blueprint[] = [];
  @state() private _notesBlueprint = '';
  @state() private _copied = '';

  override async connectedCallback() {
    super.connectedCallback();
    const [connection, blueprints] = await Promise.all([
      wayfinderFetch(this, '/design-partner'),
      serviceBlueprintFetch(this, ''),
    ]);
    if (!connection.ok) {
      this._error = `The connection details could not be loaded (${connection.status}).`;
      return;
    }
    this._connection = connection.status === 204 ? null : ((await connection.json()) as DesignPartnerConnection);
    if (blueprints.ok) {
      this._blueprints = ((await blueprints.json()) as Blueprint[]).sort((a, b) => a.displayName.localeCompare(b.displayName));
      this._notesBlueprint = this._blueprints[0]?.definitionKey ?? '';
    }
  }

  private async _copy(id: string, text: string) {
    await navigator.clipboard.writeText(text);
    this._copied = id;
  }

  private _download(fileName: string, text: string) {
    const url = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
    const link = Object.assign(document.createElement('a'), { href: url, download: fileName });
    link.click();
    URL.revokeObjectURL(url);
  }

  private _snippet(id: string, label: string, text: string, prose = false): TemplateResult {
    return html`
      <div class="snippet">
        <pre class=${prose ? 'prose' : ''} aria-label=${label}><code>${text}</code></pre>
        <uui-button
          look="secondary"
          compact
          label=${`Copy ${label}`}
          @click=${() => this._copy(id, text)}
        >${this._copied === id ? 'Copied' : 'Copy'}</uui-button>
      </div>
    `;
  }

  private _missingCallbacks(c: DesignPartnerConnection, client: DesignPartnerClient, name: string) {
    const missing = c.clients.find((setup) => setup.client === client)?.missingRedirectUris ?? [];
    if (missing.length === 0) return nothing;
    return html`
      <div class="warning" role="status">
        <p>
          This site doesn't accept ${name}'s sign-in yet. A developer adds
          ${missing.length === 1 ? 'this callback' : 'these callbacks'} to <code>Wayfinder:Mcp:RedirectUris</code> in
          <code>appsettings.json</code> and restarts the site:
        </p>
        ${this._snippet(`${client}-callbacks`, 'callback setting', redirectUrisSetting(missing))}
      </div>
    `;
  }

  private _claudeCode(c: DesignPartnerConnection) {
    return html`
      ${this._missingCallbacks(c, 'claude-code', 'Claude Code')}
      ${usesLocalCertificate(c)
        ? html`<p class="note">
            This is a local site with a development certificate, which Claude Code doesn't trust on its own. Trust it
            first, as <a href=${WALKTHROUGH_URL} target="_blank" rel="noopener">the walkthrough</a> shows.
          </p>`
        : nothing}
      <ol>
        <li>Add this site as an MCP server:${this._snippet('claude-add', 'add command', claudeCodeAdd(c))}</li>
        <li>
          Sign in. Your browser opens this backoffice; approve the request.
          ${this._snippet('claude-login', 'sign-in command', claudeCodeLogin(c))}
        </li>
        <li>Start <code>claude</code> and paste a starter prompt from below.</li>
      </ol>
      <p>
        To share the setup with a project instead, put this <code>.mcp.json</code> in its folder, then sign in as in
        step 2.
      </p>
      ${this._snippet('claude-file', '.mcp.json', claudeCodeProjectFile(c))}
      <uui-button look="outline" label="Download .mcp.json" @click=${() => this._download('.mcp.json', claudeCodeProjectFile(c))}></uui-button>
    `;
  }

  private _claudeDesktop(c: DesignPartnerConnection) {
    return html`
      ${this._missingCallbacks(c, 'claude-desktop', 'Claude Desktop')}
      <p class="note">
        Claude Desktop and claude.ai connect from Anthropic's servers, so this site must be reachable from the internet.
      </p>
      <ol>
        <li>In Claude, open <strong>Settings</strong>, then <strong>Connectors</strong>, and choose <strong>Add custom connector</strong>.</li>
        <li>Give it a name, and use this as the server URL:${this._snippet('desktop-url', 'server URL', c.serverUrl)}</li>
        <li>Under <strong>Advanced settings</strong>, set the OAuth client ID:${this._snippet('desktop-client', 'client ID', c.clientId)}</li>
        <li>Choose <strong>Connect</strong> and approve the request in this backoffice.</li>
      </ol>
    `;
  }

  private _vsCode(c: DesignPartnerConnection) {
    return html`
      ${this._missingCallbacks(c, 'vscode', 'VS Code')}
      <ol>
        <li>Save this as <code>.vscode/mcp.json</code> in your workspace:${this._snippet('vscode-file', 'mcp.json', vsCodeFile(c))}</li>
        <li>
          Start the server from the file. When VS Code asks for a client ID, enter this, and leave the client secret
          blank:${this._snippet('vscode-client', 'client ID', c.clientId)}
        </li>
        <li>Approve the request in this backoffice, then use a starter prompt in Copilot Chat's agent mode.</li>
      </ol>
      <uui-button look="outline" label="Download mcp.json" @click=${() => this._download('mcp.json', vsCodeFile(c))}></uui-button>
    `;
  }

  private _githubCopilot(c: DesignPartnerConnection) {
    return html`
      ${this._missingCallbacks(c, 'github-copilot', 'GitHub Copilot')}
      <p class="note">For Copilot Chat in VS Code, use the VS Code tab. These steps are for the Copilot CLI.</p>
      ${usesLocalCertificate(c)
        ? html`<p class="note">
            This is a local site with a development certificate, which the Copilot CLI doesn't trust on its own. Trust it
            first, as <a href=${WALKTHROUGH_URL} target="_blank" rel="noopener">the walkthrough</a> shows for Claude Code.
          </p>`
        : nothing}
      <ol>
        <li>
          Add this server to <code>~/.copilot/mcp-config.json</code>, alongside any servers already in its
          <code>mcpServers</code>:${this._snippet('copilot-file', 'mcp-config.json', copilotCliFile(c))}
        </li>
        <li>Start <code>copilot</code>. When it signs in to the server, your browser opens this backoffice; approve the request.</li>
        <li>Paste a starter prompt from below.</li>
      </ol>
    `;
  }

  private _other(c: DesignPartnerConnection) {
    return html`
      <p>Any MCP client that supports streamable HTTP and OAuth sign-in with a public client (PKCE, no secret) can connect.</p>
      <dl>
        <dt>Server URL</dt>
        <dd>${this._snippet('other-url', 'server URL', c.serverUrl)}</dd>
        <dt>OAuth client ID</dt>
        <dd>${this._snippet('other-client', 'client ID', c.clientId)}</dd>
        <dt>Sign-in callbacks this site accepts</dt>
        <dd>
          ${c.redirectUris.length
            ? html`<ul>${c.redirectUris.map((uri) => html`<li><code>${uri}</code></li>`)}</ul>
                <p class="note">A callback on <code>127.0.0.1</code> may use any port, for clients that pick their own.</p>`
            : html`<p>None yet. Add your client's callback to <code>Wayfinder:Mcp:RedirectUris</code>.</p>`}
        </dd>
      </dl>
    `;
  }

  private _setup(c: DesignPartnerConnection) {
    switch (this._client) {
      case 'claude-code':
        return this._claudeCode(c);
      case 'claude-desktop':
        return this._claudeDesktop(c);
      case 'vscode':
        return this._vsCode(c);
      case 'github-copilot':
        return this._githubCopilot(c);
      default:
        return this._other(c);
    }
  }

  private _prompts() {
    const blueprint = this._blueprints.find((b) => b.definitionKey === this._notesBlueprint);
    return html`
      <h3>Starter prompts</h3>
      <h4>Design a new service</h4>
      ${this._snippet('prompt-new', 'new service prompt', newServicePrompt(), true)}
      ${blueprint
        ? html`
            <h4>Work through my notes</h4>
            <uui-label for="notes-blueprint">Blueprint</uui-label>
            <uui-select
              id="notes-blueprint"
              label="Blueprint"
              .options=${this._blueprints.map((b) => ({
                name: b.displayName,
                value: b.definitionKey,
                selected: b.definitionKey === this._notesBlueprint,
              }))}
              @change=${(e: Event) => (this._notesBlueprint = (e.target as HTMLSelectElement).value)}
            ></uui-select>
            ${this._snippet('prompt-notes', 'notes prompt', notesPrompt(blueprint.displayName, blueprint.definitionKey), true)}
          `
        : nothing}
    `;
  }

  private _body() {
    if (this._error) return html`<p class="warning" role="alert">${this._error}</p>`;
    if (this._connection === undefined) return html`<uui-loader-bar></uui-loader-bar>`;
    if (this._connection === null) {
      return html`<p class="warning" role="status">
        MCP sign-in isn't set up on this site. A developer turns it on as the
        <a href=${MCP_SETUP_URL} target="_blank" rel="noopener">Wayfinder.Umbraco README</a> describes.
      </p>`;
    }
    const c = this._connection;
    return html`
      <p>
        An AI design partner builds and changes blueprints with you, over MCP. It signs in as you, so it can do what you
        can do on this screen and no more.
      </p>
      <uui-tab-group>
        ${CLIENTS.map(
          (client) => html`<uui-tab
            label=${client.label}
            ?active=${this._client === client.id}
            @click=${() => {
              this._client = client.id;
              this._copied = '';
            }}
          >${client.label}</uui-tab>`,
        )}
      </uui-tab-group>
      <section aria-label=${`${CLIENTS.find((x) => x.id === this._client)?.label} setup`}>${this._setup(c)}</section>
      ${this._prompts()}
    `;
  }

  render() {
    return html`
      <umb-body-layout headline="Connect a design partner">
        <uui-box>${this._body()}</uui-box>
        <uui-button slot="actions" look="primary" label="Close" @click=${() => this.modalContext?.submit()}></uui-button>
      </umb-body-layout>
    `;
  }

  static styles = css`
    uui-tab-group {
      margin-bottom: var(--uui-size-space-4, 1rem);
    }

    .snippet {
      display: flex;
      gap: var(--uui-size-space-2, 0.5rem);
      align-items: flex-start;
      margin: var(--uui-size-space-2, 0.5rem) 0;
    }

    pre {
      flex: 1;
      margin: 0;
      padding: var(--uui-size-space-3, 0.75rem);
      overflow-x: auto;
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      background: var(--uui-color-surface-alt, #f3f3f5);
      border-radius: var(--uui-border-radius, 3px);
      font-size: 0.85rem;
    }

    pre.prose,
    pre.prose code {
      font-family: inherit;
      font-size: inherit;
    }

    uui-label {
      display: block;
      margin-bottom: var(--uui-size-space-2, 0.5rem);
    }

    ol {
      padding-inline-start: 1.25rem;
    }

    li {
      margin-bottom: var(--uui-size-space-4, 1rem);
    }

    .note {
      color: var(--uui-color-text-alt, #515054);
    }

    .warning {
      padding: var(--uui-size-space-3, 0.75rem);
      border: 1px solid var(--uui-color-warning, #f0ac00);
      border-inline-start-width: 4px;
      background: var(--uui-color-surface, #fff);
    }

    h3 {
      margin-top: var(--uui-size-space-6, 1.5rem);
    }

    dd {
      margin: 0 0 var(--uui-size-space-4, 1rem);
    }
  `;
}

export default WayfinderConnectDesignPartnerModalElement;
