// Mounts the Service Requests collection into Umbraco's built-in Settings section, the exact same
// pattern service-blueprint/root/manifests.ts already uses (see that file's own remarks) — a
// menuItem against the "Advanced" settings menu, pointing at a flat "root" workspace whose only
// view is the collection.

import {
  UMB_SERVICE_REQUEST_ADMIN_COLLECTION_ALIAS,
  UMB_SERVICE_REQUEST_ADMIN_ROOT_ENTITY_TYPE,
  UMB_SERVICE_REQUEST_ADMIN_ROOT_WORKSPACE_ALIAS,
} from '../entity.js';

export const manifests = [
  {
    type: 'workspace',
    kind: 'default',
    alias: UMB_SERVICE_REQUEST_ADMIN_ROOT_WORKSPACE_ALIAS,
    name: 'Service Request Admin Root Workspace',
    meta: {
      entityType: UMB_SERVICE_REQUEST_ADMIN_ROOT_ENTITY_TYPE,
      headline: 'Service Requests',
    },
  },
  {
    type: 'workspaceView',
    kind: 'collection',
    alias: 'Wayfinder.WorkspaceView.ServiceRequestAdminRoot.Collection',
    name: 'Service Request Admin Root Collection Workspace View',
    meta: {
      label: 'Service Requests',
      pathname: 'collection',
      icon: 'icon-list',
      collectionAlias: UMB_SERVICE_REQUEST_ADMIN_COLLECTION_ALIAS,
    },
    conditions: [
      {
        alias: 'Umb.Condition.WorkspaceAlias',
        match: UMB_SERVICE_REQUEST_ADMIN_ROOT_WORKSPACE_ALIAS,
      },
    ],
  },
  {
    type: 'menuItem',
    alias: 'Wayfinder.MenuItem.ServiceRequestAdminRoot',
    name: 'Service Requests Menu Item',
    weight: 90,
    meta: {
      label: 'Service Requests',
      icon: 'icon-list',
      entityType: UMB_SERVICE_REQUEST_ADMIN_ROOT_ENTITY_TYPE,
      menus: ['Umb.Menu.AdvancedSettings'],
    },
  },
];
