import { UMB_SERVICE_BLUEPRINT_ENTITY_TYPE, UMB_SERVICE_BLUEPRINT_WORKSPACE_ALIAS } from '../entity.js';

export const manifests = [
  {
    type: 'workspace',
    kind: 'routable',
    alias: UMB_SERVICE_BLUEPRINT_WORKSPACE_ALIAS,
    name: 'Service Blueprint Workspace',
    api: () => import('./service-blueprint-workspace.context.js'),
    meta: {
      entityType: UMB_SERVICE_BLUEPRINT_ENTITY_TYPE,
    },
  },
];
