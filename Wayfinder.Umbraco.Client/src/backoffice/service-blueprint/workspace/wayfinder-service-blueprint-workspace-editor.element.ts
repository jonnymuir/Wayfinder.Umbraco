// Mounted by the workspace's own routing (see service-blueprint-workspace.context.ts's `edit/:unique`
// route) once the workspace context has loaded a specific definitionKey. Mounts
// <wayfinder-service-blueprint-editor> directly — not the shell
// (<wayfinder-service-blueprint-editor-shell>), which owns its own internal service blueprint
// list/switcher that would be entirely redundant here: the collection + workspace routing this
// file lives inside of already IS that list/switcher, at the Umbraco section level rather than
// nested inside the editor itself.
//
// <wayfinder-service-blueprint-editor> is not statically imported — it isn't npm-installed (see
// wayfinder-elements-bundle.ts) — so it's registered by loading wayfinder-elements.js at
// runtime, and this element defers rendering the tag until that registration has actually
// completed.

import { LitElement, css, html } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { loadWayfinderElements } from '../wayfinder-elements-bundle.js';
import { UmbracoWayfinderServiceBlueprintSource } from '../service-blueprint-source.js';
import type { UmbServiceBlueprintWorkspaceContext } from './service-blueprint-workspace.context.js';
import { UMB_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/workspace';

const QUEUES_URL = '/umbraco/management/api/v1/wayfinder/service-blueprints/queues';

type QueueDefinition = { queueName: string; displayName?: string };

@customElement('wayfinder-service-blueprint-workspace-editor')
export class WayfinderServiceBlueprintWorkspaceEditorElement extends UmbElementMixin(LitElement) {
  @state() private _availableQueues: QueueDefinition[] = [];
  @state() private _definitionKey: string | null = null;
  @state() private _elementsReady = false;

  private _source?: UmbracoWayfinderServiceBlueprintSource;

  connectedCallback(): void {
    super.connectedCallback();

    void loadWayfinderElements().then(() => {
      this._elementsReady = true;
    });

    this.consumeContext(UMB_AUTH_CONTEXT, (authContext) => {
      if (!authContext) return;

      this._source = new UmbracoWayfinderServiceBlueprintSource(() => authContext.getLatestToken());
      this.requestUpdate();
      void this._loadQueues(authContext);
    });

    this.consumeContext(UMB_WORKSPACE_CONTEXT, (workspaceContext) => {
      const context = workspaceContext as UmbServiceBlueprintWorkspaceContext | undefined;
      if (!context) return;

      this.observe(context.data, (data) => {
        this._definitionKey = data?.definitionKey ?? null;
      });
    });
  }

  private async _loadQueues(authContext: { getLatestToken: () => Promise<string | undefined> }): Promise<void> {
    try {
      const token = await authContext.getLatestToken();
      const response = await fetch(QUEUES_URL, {
        headers: {
          Accept: 'application/json',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        credentials: 'same-origin',
      });
      if (!response.ok) return;

      this._availableQueues = (await response.json()) as QueueDefinition[];
    } catch {
      // The editor falls back gracefully with an empty queue list — not fatal.
    }
  }

  render() {
    if (!this._elementsReady || !this._source || !this._definitionKey) {
      return html`<uui-loader></uui-loader>`;
    }

    return html`
      <wayfinder-service-blueprint-editor
        blueprint-key=${this._definitionKey}
        .serviceBlueprintSource=${this._source}
        .availableQueues=${this._availableQueues}
      ></wayfinder-service-blueprint-editor>
    `;
  }

  static styles = css`
    /* The routable workspace mounts this element directly into a clipped, fixed-height
       region — nothing above it in the backoffice chain scrolls (umb-workspace-editor,
       which normally provides the scrollable body, isn't in play here). The editor lays
       out at natural content height (its own height: 100% resolves against this auto-height
       chain), so without a scroll container of our own the content below the fold is simply
       unreachable. Make this host the scroll container. */
    :host {
      display: block;
      width: 100%;
      height: 100%;
      overflow-y: auto;
    }

    /* Outer-tree declarations beat the editor's own :host rules, which hard-code
       height: 100% + overflow: hidden for viewport-owning hosts. Here that combination
       would resolve against this host's now-definite height and clip the editor to
       exactly the container — leaving nothing for the scroll container above to scroll.
       Force natural content height instead. */
    wayfinder-service-blueprint-editor {
      display: block;
      height: auto;
      min-height: 70vh;
      overflow: visible;
    }
  `;
}

export default WayfinderServiceBlueprintWorkspaceEditorElement;
