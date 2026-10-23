import { UMB_SERVICE_BLUEPRINT_COLLECTION_ALIAS } from '../entity.js';

export const manifests = [
  {
    type: 'collectionAction',
    kind: 'button',
    alias: 'Wayfinder.CollectionAction.ServiceBlueprint.ConnectDesignPartner',
    name: 'Connect a Design Partner Collection Action',
    api: () => import('./connect-design-partner-action.js'),
    weight: 10,
    meta: {
      label: 'Connect a design partner',
      // Opens a panel, so the button shows the usual trailing ellipsis and no busy state.
      additionalOptions: true,
    },
    conditions: [
      {
        alias: 'Umb.Condition.CollectionAlias',
        match: UMB_SERVICE_BLUEPRINT_COLLECTION_ALIAS,
      },
    ],
  },
  {
    type: 'modal',
    alias: 'Wayfinder.ConnectDesignPartnerModal',
    name: 'Wayfinder Connect a Design Partner Modal',
    element: () => import('./wayfinder-connect-design-partner-modal.js'),
  },
];
