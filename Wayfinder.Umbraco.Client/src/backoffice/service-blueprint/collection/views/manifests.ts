import { UMB_SERVICE_BLUEPRINT_COLLECTION_ALIAS } from '../../entity.js';

export const UMB_SERVICE_BLUEPRINT_TABLE_COLLECTION_VIEW_ALIAS = 'Wayfinder.CollectionView.ServiceBlueprint.Table';

export const manifests = [
  {
    type: 'collectionView',
    alias: UMB_SERVICE_BLUEPRINT_TABLE_COLLECTION_VIEW_ALIAS,
    name: 'Service Blueprint Table Collection View',
    js: () => import('./wayfinder-service-blueprint-table-collection-view.element.js'),
    meta: {
      label: 'Table',
      icon: 'icon-table',
      pathName: 'table',
    },
    conditions: [
      {
        alias: 'Umb.Condition.CollectionAlias',
        match: UMB_SERVICE_BLUEPRINT_COLLECTION_ALIAS,
      },
    ],
  },
];
