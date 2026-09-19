import { manifests as viewManifests } from './views/manifests.js';
import { UMB_SERVICE_REQUEST_ADMIN_COLLECTION_ALIAS } from '../entity.js';

export const UMB_SERVICE_REQUEST_ADMIN_COLLECTION_REPOSITORY_ALIAS = 'Wayfinder.Repository.ServiceRequestAdminCollection';

export const manifests = [
  {
    type: 'collection',
    kind: 'default',
    alias: UMB_SERVICE_REQUEST_ADMIN_COLLECTION_ALIAS,
    name: 'Service Request Admin Collection',
    meta: {
      repositoryAlias: UMB_SERVICE_REQUEST_ADMIN_COLLECTION_REPOSITORY_ALIAS,
    },
  },
  {
    type: 'repository',
    alias: UMB_SERVICE_REQUEST_ADMIN_COLLECTION_REPOSITORY_ALIAS,
    name: 'Service Request Admin Collection Repository',
    api: () => import('../repository/service-request-admin-collection.repository.js'),
  },
  ...viewManifests,
];
