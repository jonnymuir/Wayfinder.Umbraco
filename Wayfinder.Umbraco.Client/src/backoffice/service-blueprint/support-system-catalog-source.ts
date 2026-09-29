// Backoffice ServiceBlueprintSupportSystemCatalog — feeds the stage-action editor's support-system
// and capability pickers. The editor's built-in HTTP fallback probes
// `{origin}/wayfinder/service-blueprint-authoring/support-systems`, a route this host doesn't
// expose, so left to that fallback the catalog loads empty and every support-system-call action
// reads "No support systems are registered on this host". The backoffice mounts the editor with
// this explicit catalog instead, pointing at the Management API route
// (ServiceBlueprintAuthoringController.GetSupportSystems), Bearer-authenticated like every other
// call from this section.

const API_BASE = '/umbraco/management/api/v1/wayfinder/service-blueprints';

type SupportSystemDescriptor = Record<string, unknown>;

export class UmbracoWayfinderSupportSystemCatalog {
  private readonly getToken: () => Promise<string | undefined>;
  private cache: Promise<SupportSystemDescriptor[]> | null = null;

  constructor(getToken: () => Promise<string | undefined>) {
    this.getToken = getToken;
  }

  // The registry freezes on first read host-side, so it can't change within an editor session.
  entries(): Promise<SupportSystemDescriptor[]> {
    this.cache ??= this.fetchEntries();
    return this.cache;
  }

  private async fetchEntries(): Promise<SupportSystemDescriptor[]> {
    const token = await this.getToken();
    const response = await fetch(`${API_BASE}/support-systems`, {
      headers: {
        Accept: 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      credentials: 'same-origin',
    });
    if (!response.ok) {
      throw new Error(`Failed to load the support system catalog (${response.status} ${response.statusText}).`);
    }
    return (await response.json()) as SupportSystemDescriptor[];
  }
}
