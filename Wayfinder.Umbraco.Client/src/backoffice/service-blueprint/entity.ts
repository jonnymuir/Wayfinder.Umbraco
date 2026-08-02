// Entity/workspace identity constants for the Wayfinder service blueprint backoffice screen — a
// flat (non-hierarchical) entity, so this mirrors Umbraco 17's own Webhook management package
// exactly: Collection + entity-actions + Workspace, mounted into the built-in Settings section's
// "Advanced" menu via a menuItem + root workspace (see root/manifests.ts), not a custom Tree or
// a standalone top-level section — the idiomatic shape for a flat admin list in Umbraco 17, and
// the same placement a host's own equivalent packages (e.g. Umbraco Prism's Tenants) now use too,
// so a backoffice user finds every non-content admin surface in one consistent place.

export const UMB_SERVICE_BLUEPRINT_ENTITY_TYPE = 'wayfinder-service-blueprint';
export const UMB_SERVICE_BLUEPRINT_ROOT_ENTITY_TYPE = 'wayfinder-service-blueprint-root';

export const UMB_SERVICE_BLUEPRINT_WORKSPACE_ALIAS = 'Wayfinder.Workspace.ServiceBlueprint';
export const UMB_SERVICE_BLUEPRINT_ROOT_WORKSPACE_ALIAS = 'Wayfinder.Workspace.ServiceBlueprintRoot';

export const UMB_SERVICE_BLUEPRINT_COLLECTION_ALIAS = 'Wayfinder.Collection.ServiceBlueprint';

export const UMB_SERVICE_BLUEPRINT_EDIT_PATH_PREFIX = 'section/settings/workspace/wayfinder-service-blueprint/edit/';

/**
 * The shape this backoffice screen works with — deliberately NOT the full
 * `AuthoredServiceBlueprint` JSON. The actual authoring surface
 * (`<wayfinder-service-blueprint-editor>`) manages its own rich local state and calls
 * `UmbracoWayfinderServiceBlueprintSource` directly for load/save — this model exists only so the
 * generic Umbraco collection/entity-action/workspace-routing machinery has something to list,
 * identify, and delete.
 */
export interface ServiceBlueprintEntityModel {
  entityType: typeof UMB_SERVICE_BLUEPRINT_ENTITY_TYPE;
  unique: string;
  definitionKey: string;
  displayName: string;
}
