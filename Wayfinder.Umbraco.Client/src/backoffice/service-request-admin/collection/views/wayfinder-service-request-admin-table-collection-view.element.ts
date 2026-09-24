import { css, html } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_COLLECTION_CONTEXT, type UmbDefaultCollectionContext } from '@umbraco-cms/backoffice/collection';
import type { ServiceRequestAdminEntityModel } from '../../entity.js';
import type { ServiceRequestAdminCollectionFilterModel, ServiceRequestAdminSort } from '../../repository/service-request-admin-collection.repository.js';

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

const SORT_STORAGE_KEY = 'Wayfinder.ServiceRequestAdmin.sort';
const DEFAULT_SORT: ServiceRequestAdminSort = 'UpdatedAtOldestFirst';
const SORT_OPTIONS: ReadonlyArray<{ name: string; value: ServiceRequestAdminSort }> = [
  { name: 'Oldest updated first (default — surfaces likely-stuck instances)', value: 'UpdatedAtOldestFirst' },
  { name: 'Newest updated first', value: 'UpdatedAtNewestFirst' },
  { name: 'Oldest created first', value: 'CreatedAtOldestFirst' },
  { name: 'Newest created first', value: 'CreatedAtNewestFirst' },
];

/** localStorage, not a cookie — this is a per-browser display preference the server never needs to see. */
function readStoredSort(): ServiceRequestAdminSort {
  try {
    const stored = localStorage.getItem(SORT_STORAGE_KEY);
    return SORT_OPTIONS.some((option) => option.value === stored) ? (stored as ServiceRequestAdminSort) : DEFAULT_SORT;
  } catch {
    return DEFAULT_SORT;
  }
}

function storeSort(sort: ServiceRequestAdminSort) {
  try {
    localStorage.setItem(SORT_STORAGE_KEY, sort);
  } catch {
    // Private browsing / blocked storage — sorting still works, it just won't be remembered next visit.
  }
}

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
  @state() private _sort: ServiceRequestAdminSort = readStoredSort();

  #collectionContext?: CollectionContextType;
  #searchDebounce?: ReturnType<typeof setTimeout>;

  constructor() {
    super();
    this.consumeContext(UMB_COLLECTION_CONTEXT, (context) => {
      this.#collectionContext = context as CollectionContextType;
      this.observe(context?.items, (items) => this.#createTableItems((items ?? []) as ServiceRequestAdminEntityModel[]), 'wayfinderServiceRequestAdminCollectionItems');
      // The collection context's own initial fetch only knows the generic UmbCollectionFilterModel
      // shape, so it never carries our custom `sort` param — push the persisted (or default) sort
      // explicitly once, so the very first request honours it too, not just the dropdown's display.
      this.#collectionContext.setFilter({ sort: this._sort });
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
      this.#collectionContext?.setFilter({ filter: value, includeAborted: this._includeAborted, sort: this._sort });
    }, 300);
  }

  #onIncludeAbortedChange(event: Event) {
    this._includeAborted = (event.target as HTMLInputElement).checked;
    this.#collectionContext?.setFilter({ filter: this._searchText, includeAborted: this._includeAborted, sort: this._sort });
  }

  #onSortChange(event: Event) {
    this._sort = (event.target as HTMLSelectElement).value as ServiceRequestAdminSort;
    storeSort(this._sort);
    this.#collectionContext?.setFilter({ filter: this._searchText, includeAborted: this._includeAborted, sort: this._sort });
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
        <uui-select
          label="Sort by"
          .options=${SORT_OPTIONS.map((option) => ({ ...option, selected: option.value === this._sort }))}
          @change=${this.#onSortChange}
        ></uui-select>
      </div>
      <umb-table .columns=${this._tableColumns} .items=${this._tableItems}></umb-table>
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
