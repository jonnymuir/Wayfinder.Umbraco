import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbItemServerDataSourceBase } from '@umbraco-cms/backoffice/repository';
import { serviceBlueprintFetch } from '../../service-blueprint-http.js';
import { UMB_SERVICE_BLUEPRINT_ENTITY_TYPE, type ServiceBlueprintEntityModel } from '../../entity.js';

type ServerSummary = { definitionKey: string; displayName: string };

/**
 * Only used by the built-in `kind: 'delete'` entityAction, to show the service blueprint's display name
 * in its confirmation dialog — this entity has no picker/relation use case, so nothing else
 * needs this tier. Reuses the list endpoint (list is already cheap and small; no bespoke
 * "get by ids" endpoint needed).
 */
export class UmbServiceBlueprintItemServerDataSource extends UmbItemServerDataSourceBase<ServerSummary, ServiceBlueprintEntityModel> {
  constructor(host: UmbControllerHost) {
    super(host, {
      mapper: (item) => ({
        entityType: UMB_SERVICE_BLUEPRINT_ENTITY_TYPE,
        unique: item.definitionKey,
        definitionKey: item.definitionKey,
        displayName: item.displayName,
      }),
      getItems: async (uniques) => {
        const response = await serviceBlueprintFetch(host, '');
        if (!response.ok) {
          return { data: undefined, error: new Error(`Failed to list service blueprints (${response.status}).`) };
        }
        const all = (await response.json()) as ServerSummary[];
        const wanted = new Set(uniques);
        return { data: all.filter((item) => wanted.has(item.definitionKey)) };
      },
    });
  }
}

export default UmbServiceBlueprintItemServerDataSource;
