// Entity/workspace identity constants for the Wayfinder service blueprint backoffice screen — a
// flat (non-hierarchical) entity, so this mirrors Umbraco 17's own Webhook management package
// (Collection + entity-actions + Workspace) rather than a custom Tree, which is the idiomatic
// shape for a flat list in Umbraco 17.

export const UMB_SERVICE_BLUEPRINT_ENTITY_TYPE = 'wayfinder-service-blueprint';
export const UMB_SERVICE_BLUEPRINT_ROOT_ENTITY_TYPE = 'wayfinder-service-blueprint-root';

export const UMB_SERVICE_BLUEPRINT_WORKSPACE_ALIAS = 'Wayfinder.Workspace.ServiceBlueprint';

export const UMB_SERVICE_BLUEPRINT_COLLECTION_ALIAS = 'Wayfinder.Collection.ServiceBlueprint';

export const UMB_SERVICE_BLUEPRINT_EDIT_PATH_PREFIX = 'section/blueprints/workspace/wayfinder-service-blueprint/edit/';

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
