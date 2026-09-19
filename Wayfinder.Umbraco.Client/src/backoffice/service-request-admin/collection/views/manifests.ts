import { UMB_SERVICE_REQUEST_ADMIN_COLLECTION_ALIAS } from '../../entity.js';

export const UMB_SERVICE_REQUEST_ADMIN_TABLE_COLLECTION_VIEW_ALIAS = 'Wayfinder.CollectionView.ServiceRequestAdmin.Table';

export const manifests = [
  {
    type: 'collectionView',
    alias: UMB_SERVICE_REQUEST_ADMIN_TABLE_COLLECTION_VIEW_ALIAS,
    name: 'Service Request Admin Table Collection View',
    js: () => import('./wayfinder-service-request-admin-table-collection-view.element.js'),
    meta: {
      label: 'Table',
      icon: 'icon-list',
      pathName: 'table',
    },
    conditions: [
      {
        alias: 'Umb.Condition.CollectionAlias',
        match: UMB_SERVICE_REQUEST_ADMIN_COLLECTION_ALIAS,
      },
    ],
  },
];
