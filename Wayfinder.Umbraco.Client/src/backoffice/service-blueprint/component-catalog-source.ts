// Backoffice ServiceBlueprintComponentCatalog — the schema behind the editor's properties-panel
// add/edit UI and the input-field set its client-side checks derive. The editor's built-in HTTP
// fallback probes `{origin}/wayfinder/service-blueprint-authoring/component-types`, a route this
// host doesn't expose; left to that fallback the catalog loads empty and the editor's
// calc-reference validation flags every real field as unknown. So the backoffice mounts the
// editor with this explicit catalog, pointing at the Management API route
// (ServiceBlueprintAuthoringController.GetComponentTypes), Bearer-authenticated like every other
// call from this section.

const API_BASE = '/umbraco/management/api/v1/wayfinder/service-blueprints';

type ComponentDescriptor = Record<string, unknown>;

export class UmbracoWayfinderComponentCatalog {
  private readonly getToken: () => Promise<string | undefined>;
  private cache: Promise<ComponentDescriptor[]> | null = null;

  constructor(getToken: () => Promise<string | undefined>) {
    this.getToken = getToken;
  }

  // The registry freezes on first read host-side, so it can't change within an editor session —
  // cache the first fetch for this instance's lifetime, matching the editor's own
  // HttpServiceBlueprintComponentCatalog.
  entries(): Promise<ComponentDescriptor[]> {
    this.cache ??= this.fetchEntries();
    return this.cache;
  }

  private async fetchEntries(): Promise<ComponentDescriptor[]> {
    const token = await this.getToken();
    const response = await fetch(`${API_BASE}/component-types`, {
      headers: {
        Accept: 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      credentials: 'same-origin',
    });
    if (!response.ok) {
      throw new Error(`Failed to load the component type catalog (${response.status} ${response.statusText}).`);
    }
    return (await response.json()) as ComponentDescriptor[];
  }
}
