import { manifests as viewManifests } from './views/manifests.js';
import { manifests as actionManifests } from './action/manifests.js';
import { UMB_SERVICE_BLUEPRINT_COLLECTION_ALIAS } from '../entity.js';

export const UMB_SERVICE_BLUEPRINT_COLLECTION_REPOSITORY_ALIAS = 'Wayfinder.Repository.ServiceBlueprintCollection';

export const manifests = [
  {
    type: 'collection',
    kind: 'default',
    alias: UMB_SERVICE_BLUEPRINT_COLLECTION_ALIAS,
    name: 'Service Blueprint Collection',
    meta: {
      repositoryAlias: UMB_SERVICE_BLUEPRINT_COLLECTION_REPOSITORY_ALIAS,
    },
  },
  {
    type: 'repository',
    alias: UMB_SERVICE_BLUEPRINT_COLLECTION_REPOSITORY_ALIAS,
    name: 'Service Blueprint Collection Repository',
    api: () => import('./service-blueprint-collection.repository.js'),
  },
  ...viewManifests,
  ...actionManifests,
];
