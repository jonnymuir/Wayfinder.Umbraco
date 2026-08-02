// Mounts the Blueprints collection into Umbraco's built-in Settings section — the exact shape
// Umbraco's own Webhook management package uses (see @umbraco-cms/backoffice's
// packages/webhook/webhook-root/): a menuItem registered against Umbraco's own
// "Advanced" settings menu, pointing at a flat "root" workspace whose only view is the
// existing service blueprint collection. Reaching the collection through a real menuItem click
// (rather than a raw window.location redirect, or hosting it as a Content-section dashboard
// tab) is what correctly seeds UMB_ENTITY_CONTEXT before the collection view ever renders —
// see the now-removed wayfinder-service-blueprint-tab.element.ts's own remarks for the
// workaround this replaces.

import {
  UMB_SERVICE_BLUEPRINT_COLLECTION_ALIAS,
  UMB_SERVICE_BLUEPRINT_ROOT_ENTITY_TYPE,
  UMB_SERVICE_BLUEPRINT_ROOT_WORKSPACE_ALIAS,
} from '../entity.js';

export const manifests = [
  {
    type: 'workspace',
    kind: 'default',
    alias: UMB_SERVICE_BLUEPRINT_ROOT_WORKSPACE_ALIAS,
    name: 'Service Blueprint Root Workspace',
    meta: {
      entityType: UMB_SERVICE_BLUEPRINT_ROOT_ENTITY_TYPE,
      headline: 'Blueprints',
    },
  },
  {
    type: 'workspaceView',
    kind: 'collection',
    alias: 'Wayfinder.WorkspaceView.ServiceBlueprintRoot.Collection',
    name: 'Service Blueprint Root Collection Workspace View',
    meta: {
      label: 'Blueprints',
      pathname: 'collection',
      icon: 'icon-diagram',
      collectionAlias: UMB_SERVICE_BLUEPRINT_COLLECTION_ALIAS,
    },
    conditions: [
      {
        alias: 'Umb.Condition.WorkspaceAlias',
        match: UMB_SERVICE_BLUEPRINT_ROOT_WORKSPACE_ALIAS,
      },
    ],
  },
  {
    type: 'menuItem',
    alias: 'Wayfinder.MenuItem.ServiceBlueprintRoot',
    name: 'Blueprints Menu Item',
    weight: 100,
    meta: {
      label: 'Blueprints',
      icon: 'icon-diagram',
      entityType: UMB_SERVICE_BLUEPRINT_ROOT_ENTITY_TYPE,
      menus: ['Umb.Menu.AdvancedSettings'],
    },
  },
];
