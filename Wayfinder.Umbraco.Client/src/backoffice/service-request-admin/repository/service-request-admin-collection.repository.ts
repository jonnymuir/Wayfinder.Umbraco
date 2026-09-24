import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbRepositoryBase } from '@umbraco-cms/backoffice/repository';
import type { UmbCollectionFilterModel } from '@umbraco-cms/backoffice/collection';
import { serviceRequestAdminFetch } from '../service-request-admin-http.js';
import { UMB_SERVICE_REQUEST_ADMIN_ENTITY_TYPE, type ServiceRequestAdminEntityModel } from '../entity.js';

/** Matches the server's ServiceRequestAdminSort enum (Wayfinder/Models/ServiceDesign/ServiceRequestAdminListEnvelope.cs). */
export type ServiceRequestAdminSort =
  | 'UpdatedAtOldestFirst'
  | 'UpdatedAtNewestFirst'
  | 'CreatedAtOldestFirst'
  | 'CreatedAtNewestFirst';

/** Extends the standard collection filter with the admin-specific controls this screen needs. */
export interface ServiceRequestAdminCollectionFilterModel extends UmbCollectionFilterModel {
  includeAborted?: boolean;
  sort?: ServiceRequestAdminSort;
}

interface ServerSummary {
  instanceId: string;
  blueprintKey: string;
  blueprintDisplayName: string;
  tenantId: string;
  userId: string;
  currentStage: string;
  currentStateDisplayName: string;
  isCompleted: boolean;
  isAborted: boolean;
  abortedAt: string | null;
  abortedReason: string | null;
  abortedByUserId: string | null;
  createdAt: string;
  updatedAt: string;
}

interface ServerListResponse {
  items: ServerSummary[];
  totalMatchingCount: number;
}

export class UmbServiceRequestAdminCollectionRepository extends UmbRepositoryBase {
  #host: UmbControllerHost;

  constructor(host: UmbControllerHost) {
    super(host);
    this.#host = host;
  }

  async requestCollection(filter: ServiceRequestAdminCollectionFilterModel = {}) {
    const params = new URLSearchParams();
    if (filter.filter) {
      params.set('searchText', filter.filter);
    }
    if (filter.includeAborted) {
      params.set('includeAborted', 'true');
    }
    if (filter.sort) {
      params.set('sort', filter.sort);
    }
    if (filter.skip !== undefined && filter.take) {
      params.set('pageIndex', String(Math.floor(filter.skip / filter.take)));
      params.set('pageSize', String(filter.take));
    }

    const response = await serviceRequestAdminFetch(this.#host, `?${params.toString()}`);
    if (!response.ok) {
      return { error: new Error(`Failed to list service requests (${response.status}).`) };
    }

    const result = (await response.json()) as ServerListResponse;
    const items: ServiceRequestAdminEntityModel[] = result.items.map((item) => ({
      entityType: UMB_SERVICE_REQUEST_ADMIN_ENTITY_TYPE as typeof UMB_SERVICE_REQUEST_ADMIN_ENTITY_TYPE,
      unique: item.instanceId,
      ...item,
    }));

    return { data: { items, total: result.totalMatchingCount } };
  }
}

export default UmbServiceRequestAdminCollectionRepository;
