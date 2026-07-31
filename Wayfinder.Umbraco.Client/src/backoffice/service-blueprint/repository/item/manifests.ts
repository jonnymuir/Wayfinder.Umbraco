export const UMB_SERVICE_BLUEPRINT_ITEM_REPOSITORY_ALIAS = 'Wayfinder.Repository.ServiceBlueprintItem';
const UMB_SERVICE_BLUEPRINT_ITEM_STORE_ALIAS = 'Wayfinder.Store.ServiceBlueprintItem';

export const manifests = [
  {
    type: 'repository',
    alias: UMB_SERVICE_BLUEPRINT_ITEM_REPOSITORY_ALIAS,
    name: 'Service Blueprint Item Repository',
    api: () => import('./service-blueprint-item.repository.js'),
  },
  {
    type: 'itemStore',
    alias: UMB_SERVICE_BLUEPRINT_ITEM_STORE_ALIAS,
    name: 'Service Blueprint Item Store',
    api: () => import('./service-blueprint-item.store.js'),
  },
];
