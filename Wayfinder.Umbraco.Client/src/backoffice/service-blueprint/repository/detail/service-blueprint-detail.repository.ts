import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbDetailRepositoryBase } from '@umbraco-cms/backoffice/repository';
import { UmbServiceBlueprintDetailServerDataSource } from './service-blueprint-detail.server.data-source.js';
import { UMB_SERVICE_BLUEPRINT_DETAIL_STORE_CONTEXT } from './service-blueprint-detail.store.js';
import type { ServiceBlueprintEntityModel } from '../../entity.js';

export class UmbServiceBlueprintDetailRepository extends UmbDetailRepositoryBase<ServiceBlueprintEntityModel> {
  constructor(host: UmbControllerHost) {
    super(host, UmbServiceBlueprintDetailServerDataSource, UMB_SERVICE_BLUEPRINT_DETAIL_STORE_CONTEXT);
  }

  /** Service blueprint definitions are not nested under a parent — always create at the root. */
  async create(model: ServiceBlueprintEntityModel) {
    return super.create(model, null);
  }
}

export default UmbServiceBlueprintDetailRepository;
