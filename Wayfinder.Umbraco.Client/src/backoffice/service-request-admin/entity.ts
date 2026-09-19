// Entity/workspace identity constants for the Wayfinder instance admin backoffice screen — same
// flat, non-hierarchical shape as the service-blueprint screen (see that folder's own entity.ts),
// mounted into Umbraco's built-in Settings section's "Advanced" menu the same way. Deliberately
// no detail/item repositories or per-row workspace: this screen never edits or creates an
// instance, only lists/searches/soft-aborts one — there's nothing to drill into.

export const UMB_SERVICE_REQUEST_ADMIN_ROOT_ENTITY_TYPE = 'wayfinder-service-request-admin-root';
export const UMB_SERVICE_REQUEST_ADMIN_ENTITY_TYPE = 'wayfinder-service-request-admin';

export const UMB_SERVICE_REQUEST_ADMIN_ROOT_WORKSPACE_ALIAS = 'Wayfinder.Workspace.ServiceRequestAdminRoot';

export const UMB_SERVICE_REQUEST_ADMIN_COLLECTION_ALIAS = 'Wayfinder.Collection.ServiceRequestAdmin';

/**
 * The shape this backoffice screen works with — mirrors
 * `Services.ServiceRequestAdminSummary` (Wayfinder.Engine) field-for-field, plus the
 * `entityType`/`unique` every generic Umbraco collection/entity-action needs to identify a row.
 */
export interface ServiceRequestAdminEntityModel {
  entityType: typeof UMB_SERVICE_REQUEST_ADMIN_ENTITY_TYPE;
  unique: string;
  instanceId: string;
  blueprintKey: string;
  blueprintDisplayName: string;
  tenantId: string;
  userId: string;
  currentStage: string;
  currentStateDisplayName: string;
  isCompleted: boolean;
  isAborted: boolean;
  abortedAt: string | null;
  abortedReason: string | null;
  abortedByUserId: string | null;
  createdAt: string;
  updatedAt: string;
}
