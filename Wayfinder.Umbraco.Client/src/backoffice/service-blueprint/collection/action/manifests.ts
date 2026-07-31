import { UMB_SERVICE_BLUEPRINT_ROOT_ENTITY_TYPE, UMB_SERVICE_BLUEPRINT_COLLECTION_ALIAS } from '../../entity.js';

export const manifests = [
  {
    type: 'collectionAction',
    kind: 'create',
    alias: 'Wayfinder.CollectionAction.ServiceBlueprint.Create',
    name: 'Create Service Blueprint Collection Action',
    conditions: [
      {
        alias: 'Umb.Condition.CollectionAlias',
        match: UMB_SERVICE_BLUEPRINT_COLLECTION_ALIAS,
      },
    ],
  },
  {
    type: 'entityCreateOptionAction',
    alias: 'Wayfinder.EntityCreateOptionAction.ServiceBlueprint',
    name: 'Create Service Blueprint Option Action',
    api: () => import('./service-blueprint-create-option-action.js'),
    forEntityTypes: [UMB_SERVICE_BLUEPRINT_ROOT_ENTITY_TYPE],
    meta: {
      icon: 'icon-diagram',
      label: 'New service blueprint',
      description: 'Author a new service blueprint definition, hosted and run entirely in Umbraco.',
    },
  },
];
