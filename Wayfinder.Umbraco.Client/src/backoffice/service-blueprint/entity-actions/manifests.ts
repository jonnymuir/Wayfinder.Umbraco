import { UMB_SERVICE_BLUEPRINT_ENTITY_TYPE } from '../entity.js';
import { UMB_SERVICE_BLUEPRINT_DETAIL_REPOSITORY_ALIAS } from '../repository/detail/manifests.js';
import { UMB_SERVICE_BLUEPRINT_ITEM_REPOSITORY_ALIAS } from '../repository/item/manifests.js';

export const manifests = [
  {
    type: 'entityAction',
    kind: 'delete',
    alias: 'Wayfinder.EntityAction.ServiceBlueprint.Delete',
    name: 'Delete Service Blueprint Entity Action',
    forEntityTypes: [UMB_SERVICE_BLUEPRINT_ENTITY_TYPE],
    meta: {
      detailRepositoryAlias: UMB_SERVICE_BLUEPRINT_DETAIL_REPOSITORY_ALIAS,
      itemRepositoryAlias: UMB_SERVICE_BLUEPRINT_ITEM_REPOSITORY_ALIAS,
    },
  },
];
