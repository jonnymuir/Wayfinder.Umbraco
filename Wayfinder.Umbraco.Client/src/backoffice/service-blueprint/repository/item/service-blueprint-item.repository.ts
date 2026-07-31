import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbItemRepositoryBase } from '@umbraco-cms/backoffice/repository';
import { UmbServiceBlueprintItemServerDataSource } from './service-blueprint-item.server.data-source.js';
import { UMB_SERVICE_BLUEPRINT_ITEM_STORE_CONTEXT } from './service-blueprint-item.store.js';
import type { ServiceBlueprintEntityModel } from '../../entity.js';

export class UmbServiceBlueprintItemRepository extends UmbItemRepositoryBase<ServiceBlueprintEntityModel> {
  constructor(host: UmbControllerHost) {
    super(host, UmbServiceBlueprintItemServerDataSource, UMB_SERVICE_BLUEPRINT_ITEM_STORE_CONTEXT);
  }
}

export default UmbServiceBlueprintItemRepository;
