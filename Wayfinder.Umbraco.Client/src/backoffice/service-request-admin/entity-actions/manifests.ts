import { UMB_SERVICE_REQUEST_ADMIN_ENTITY_TYPE } from '../entity.js';

export const manifests = [
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'Wayfinder.EntityAction.ServiceRequestAdmin.Abort',
    name: 'Abort Service Request Entity Action',
    forEntityTypes: [UMB_SERVICE_REQUEST_ADMIN_ENTITY_TYPE],
    api: () => import('./wayfinder-abort-service-request-entity-action.js'),
    meta: {
      icon: 'icon-block',
      label: 'Stop',
    },
  },
];
