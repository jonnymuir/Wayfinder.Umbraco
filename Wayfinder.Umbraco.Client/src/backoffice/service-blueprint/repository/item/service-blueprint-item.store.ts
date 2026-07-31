import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';
import { UmbItemStoreBase } from '@umbraco-cms/backoffice/store';
import type { ServiceBlueprintEntityModel } from '../../entity.js';

export class UmbServiceBlueprintItemStore extends UmbItemStoreBase<ServiceBlueprintEntityModel> {
  constructor(host: UmbControllerHost) {
    super(host, UMB_SERVICE_BLUEPRINT_ITEM_STORE_CONTEXT.toString());
  }
}

export default UmbServiceBlueprintItemStore;

export const UMB_SERVICE_BLUEPRINT_ITEM_STORE_CONTEXT = new UmbContextToken<UmbServiceBlueprintItemStore>(
  'UmbServiceBlueprintItemStore',
);
