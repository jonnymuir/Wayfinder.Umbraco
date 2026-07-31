import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';
import { UmbDetailStoreBase } from '@umbraco-cms/backoffice/store';
import type { ServiceBlueprintEntityModel } from '../../entity.js';

export class UmbServiceBlueprintDetailStore extends UmbDetailStoreBase<ServiceBlueprintEntityModel> {
  constructor(host: UmbControllerHost) {
    super(host, UMB_SERVICE_BLUEPRINT_DETAIL_STORE_CONTEXT.toString());
  }
}

export default UmbServiceBlueprintDetailStore;

export const UMB_SERVICE_BLUEPRINT_DETAIL_STORE_CONTEXT = new UmbContextToken<UmbServiceBlueprintDetailStore>(
  'UmbServiceBlueprintDetailStore',
);
