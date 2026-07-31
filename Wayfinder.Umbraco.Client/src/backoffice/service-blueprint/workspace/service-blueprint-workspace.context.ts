import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbEntityDetailWorkspaceContextBase } from '@umbraco-cms/backoffice/workspace';
import { UMB_SERVICE_BLUEPRINT_ENTITY_TYPE, UMB_SERVICE_BLUEPRINT_WORKSPACE_ALIAS, type ServiceBlueprintEntityModel } from '../entity.js';
import { UMB_SERVICE_BLUEPRINT_DETAIL_REPOSITORY_ALIAS } from '../repository/detail/manifests.js';

/**
 * Deliberately minimal — only enough to resolve "which definitionKey is this route editing" for
 * the details view to hand to `<wayfinder-service-blueprint-editor>`. There is no "create" route
 * (creation happens through a dedicated modal collecting the definitionKey upfront — see
 * `create-modal/` — since unlike Umbraco's own entities this one's identity is a human-chosen
 * slug, not a random GUID minted after the fact) and no generic Save workspaceAction is
 * registered: the editor owns its own save flow via `UmbracoWayfinderServiceBlueprintSource` entirely
 * independently of this context's `data`/`submit()` machinery.
 */
export class UmbServiceBlueprintWorkspaceContext extends UmbEntityDetailWorkspaceContextBase<ServiceBlueprintEntityModel> {
  constructor(host: UmbControllerHost) {
    super(host, {
      entityType: UMB_SERVICE_BLUEPRINT_ENTITY_TYPE,
      workspaceAlias: UMB_SERVICE_BLUEPRINT_WORKSPACE_ALIAS,
      detailRepositoryAlias: UMB_SERVICE_BLUEPRINT_DETAIL_REPOSITORY_ALIAS,
    });

    this.routes.setRoutes([
      {
        path: 'edit/:unique',
        component: () => import('./wayfinder-service-blueprint-workspace-editor.element.js'),
        setup: (_component, info) => {
          this.load(info.match.params.unique);
        },
      },
    ]);
  }

  /** Definition key of the service blueprint currently loaded into this workspace, if any. */
  getDefinitionKey(): string | undefined {
    return this.getData()?.definitionKey;
  }
}

export { UmbServiceBlueprintWorkspaceContext as api };
export default UmbServiceBlueprintWorkspaceContext;
