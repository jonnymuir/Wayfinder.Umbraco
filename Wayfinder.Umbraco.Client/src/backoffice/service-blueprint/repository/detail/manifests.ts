export const UMB_SERVICE_BLUEPRINT_DETAIL_REPOSITORY_ALIAS = 'Wayfinder.Repository.ServiceBlueprintDetail';
const UMB_SERVICE_BLUEPRINT_DETAIL_STORE_ALIAS = 'Wayfinder.Store.ServiceBlueprintDetail';

export const manifests = [
  {
    type: 'repository',
    alias: UMB_SERVICE_BLUEPRINT_DETAIL_REPOSITORY_ALIAS,
    name: 'Service Blueprint Detail Repository',
    api: () => import('./service-blueprint-detail.repository.js'),
  },
  {
    type: 'store',
    alias: UMB_SERVICE_BLUEPRINT_DETAIL_STORE_ALIAS,
    name: 'Service Blueprint Detail Store',
    api: () => import('./service-blueprint-detail.store.js'),
  },
];
