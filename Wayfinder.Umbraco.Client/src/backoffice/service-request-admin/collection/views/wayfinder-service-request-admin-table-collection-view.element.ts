import { css, html } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_COLLECTION_CONTEXT, type UmbDefaultCollectionContext } from '@umbraco-cms/backoffice/collection';
import type { ServiceRequestAdminEntityModel } from '../../entity.js';
import type { ServiceRequestAdminCollectionFilterModel } from '../../repository/service-request-admin-collection.repository.js';

interface TableColumn {
  name: string;
  alias: string;
  align?: string;
}

interface TableItem {
  id: string;
  icon: string;
  data: Array<{ columnAlias: string; value: unknown }>;
}

type CollectionContextType = UmbDefaultCollectionContext<ServiceRequestAdminEntityModel, ServiceRequestAdminCollectionFilterModel>;

/**
 * Deliberately no generic Umbraco search-toolbar action here — the "include aborted" toggle is
 * specific to this screen and needs to sit right next to the search box, so both are just rendered
 * directly by this view, debounced (300ms) and pushed to the collection context's own setFilter —
 * the same mechanism the built-in search toolbar would use, just owned locally instead of via a
 * separate registered extension. See ServiceRequestAdminCollectionFilterModel for the shape.
 */
@customElement('wayfinder-service-request-admin-table-collection-view')
export class WayfinderServiceRequestAdminTableCollectionViewElement extends UmbLitElement {
  @state() private _tableColumns: TableColumn[] = [
    { name: 'Blueprint', alias: 'blueprint' },
    { name: 'Stage', alias: 'stage' },
    { name: 'Status', alias: 'status' },
    { name: 'Tenant / User', alias: 'owner' },
    { name: 'Last updated', alias: 'updatedAt' },
    { name: '', alias: 'entityActions', align: 'right' },
  ];

  @state() private _tableItems: TableItem[] = [];
  @state() private _searchText = '';
  @state() private _includeAborted = false;

  #collectionContext?: CollectionContextType;
  #searchDebounce?: ReturnType<typeof setTimeout>;

  constructor() {
    super();
    this.consumeContext(UMB_COLLECTION_CONTEXT, (context) => {
      this.#collectionContext = context as CollectionContextType;
      this.observe(context?.items, (items) => this.#createTableItems((items ?? []) as ServiceRequestAdminEntityModel[]), 'wayfinderServiceRequestAdminCollectionItems');
    });
  }

  #createTableItems(instances: ServiceRequestAdminEntityModel[]) {
    this._tableItems = instances.map((instance) => ({
      id: instance.unique,
      icon: instance.isAborted ? 'icon-block' : instance.isCompleted ? 'icon-check' : 'icon-time',
      data: [
        {
          columnAlias: 'blueprint',
          value: html`${instance.blueprintDisplayName}
            <div style="color: var(--uui-color-text-alt, #888); font-size: 12px;">${instance.instanceId}</div>`,
        },
        { columnAlias: 'stage', value: instance.currentStateDisplayName || instance.currentStage },
        { columnAlias: 'status', value: this.#statusLabel(instance) },
        {
          columnAlias: 'owner',
          value: html`${instance.tenantId}
            <div style="color: var(--uui-color-text-alt, #888); font-size: 12px;">${instance.userId}</div>`,
        },
        { columnAlias: 'updatedAt', value: new Date(instance.updatedAt).toLocaleString() },
        {
          columnAlias: 'entityActions',
          value: html`<umb-entity-actions-table-column-view
            .value=${{ entityType: instance.entityType, unique: instance.unique, name: instance.blueprintDisplayName }}
          ></umb-entity-actions-table-column-view>`,
        },
      ],
    }));
  }

  #statusLabel(instance: ServiceRequestAdminEntityModel) {
    if (instance.isAborted) {
      return html`<uui-tag color="danger" look="secondary"
        title=${instance.abortedReason ? `${instance.abortedByUserId ?? 'admin'}: ${instance.abortedReason}` : ''}
        >Aborted</uui-tag
      >`;
    }
    if (instance.isCompleted) {
      return html`<uui-tag color="positive" look="secondary">Completed</uui-tag>`;
    }
    return html`<uui-tag color="warning" look="secondary">In progress</uui-tag>`;
  }

  #onSearchInput(event: InputEvent) {
    const value = (event.target as HTMLInputElement).value;
    clearTimeout(this.#searchDebounce);
    this.#searchDebounce = setTimeout(() => {
      this._searchText = value;
      this.#collectionContext?.setFilter({ filter: value, includeAborted: this._includeAborted });
    }, 300);
  }

  #onIncludeAbortedChange(event: Event) {
    this._includeAborted = (event.target as HTMLInputElement).checked;
    this.#collectionContext?.setFilter({ filter: this._searchText, includeAborted: this._includeAborted });
  }

  render() {
    return html`
      <div class="toolbar">
        <uui-input
          label="Search"
          placeholder="Search by instance id, blueprint, stage or user…"
          @input=${this.#onSearchInput}
        ></uui-input>
        <uui-checkbox label="Include aborted" @change=${this.#onIncludeAbortedChange}></uui-checkbox>
      </div>
      <umb-table .columns=${this._tableColumns} .items=${this._tableItems}></umb-table>
      <umb-collection-pagination></umb-collection-pagination>
    `;
  }

  static styles = css`
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--uui-size-space-4, 12px);
    }
    .toolbar {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-4, 12px);
      padding: 0 var(--uui-size-space-4, 12px);
    }
    uui-input {
      flex: 1 1 auto;
      max-width: 400px;
    }
  `;
}

export default WayfinderServiceRequestAdminTableCollectionViewElement;
