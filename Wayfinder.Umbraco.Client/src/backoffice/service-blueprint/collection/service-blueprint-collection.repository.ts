import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbRepositoryBase } from '@umbraco-cms/backoffice/repository';
import { serviceBlueprintFetch } from '../service-blueprint-http.js';
import { UMB_SERVICE_BLUEPRINT_ENTITY_TYPE, type ServiceBlueprintEntityModel } from '../entity.js';

type ServerSummary = { definitionKey: string; displayName: string };

/**
 * The list is expected to stay small (a handful of authored service blueprints, not hundreds),
 * so paging is applied client-side against the one cheap list endpoint rather than adding
 * server-side paging query params this screen doesn't need yet.
 */
export class UmbServiceBlueprintCollectionRepository extends UmbRepositoryBase {
  #host: UmbControllerHost;

  constructor(host: UmbControllerHost) {
    super(host);
    this.#host = host;
  }

  async requestCollection(filter: { skip?: number; take?: number } = {}) {
    const response = await serviceBlueprintFetch(this.#host, '');
    if (!response.ok) {
      return { error: new Error(`Failed to list service blueprints (${response.status}).`) };
    }

    const all = (await response.json()) as ServerSummary[];
    const items: ServiceBlueprintEntityModel[] = all
      .map((item) => ({
        entityType: UMB_SERVICE_BLUEPRINT_ENTITY_TYPE as typeof UMB_SERVICE_BLUEPRINT_ENTITY_TYPE,
        unique: item.definitionKey,
        definitionKey: item.definitionKey,
        displayName: item.displayName,
      }))
      .sort((a, b) => a.displayName.localeCompare(b.displayName));

    const skip = filter.skip ?? 0;
    const take = filter.take ?? items.length;

    return { data: { items: items.slice(skip, skip + take), total: items.length } };
  }
}

export default UmbServiceBlueprintCollectionRepository;
