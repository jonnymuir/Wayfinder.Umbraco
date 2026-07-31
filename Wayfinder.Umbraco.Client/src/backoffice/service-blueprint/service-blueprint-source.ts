// Backoffice ServiceBlueprintSource — the editor host implementation Wayfinder.Umbraco wires into
// <wayfinder-service-blueprint-editor>. Unlike a business-app host (cookie-auth, same-origin), the
// backoffice's Management API is Bearer-token authenticated, and the token can rotate between
// calls — `getToken` is called fresh on every request rather than captured once at construction,
// so a long-open editor session never sends a stale token.
//
// The editor bundle (wayfinder-elements.js, from the Wayfinder.Editor NuGet package's static web
// assets) is loaded at runtime by URL, not npm-installed — see loadWayfinderElements() in
// wayfinder-elements-bundle.js. That means this file has no compile-time type import from it
// either: ServiceBlueprintSaveError/sanitise*/hydrateServiceBlueprintDefinition/
// serializeAuthoredServiceBlueprint are all read off the dynamically-imported module at runtime,
// and the wire shapes below are typed loosely (Record<string, unknown> / the handful of fields
// this file actually branches on) rather than duplicating the editor's own large canonical types.

import { loadWayfinderElements } from './wayfinder-elements-bundle.js';

const API_BASE = '/umbraco/management/api/v1/wayfinder/service-blueprints';

type AuthoredServiceBlueprint = Record<string, unknown> & { definitionKey: string; displayName: string; version: number };

type ProblemDetailsPayload = {
  title?: unknown;
  detail?: unknown;
  status?: unknown;
  traceId?: unknown;
  summary?: unknown;
  message?: unknown;
  errors?: unknown;
  extensions?: {
    traceId?: unknown;
    errors?: unknown;
  };
};

// The shape Wayfinder.Engine.Services.ServiceBlueprintSaveOutcome serializes to.
type ServiceBlueprintSaveOutcomePayload = {
  status?: unknown;
  diagnostics?: unknown;
  currentVersion?: unknown;
  newVersion?: unknown;
};

// A ServiceBlueprintDiagnostic's `path` names the offending element with stable keys, e.g.
// "stages.licence-details.components[0].items[0].fieldKey" or "calculations.fields.member".
// Only the "stages.<key>..." shape names something the canvas can actually jump to.
function stageKeyFromDiagnosticPath(path: unknown): string | undefined {
  if (typeof path !== 'string') {
    return undefined;
  }
  return /^stages\.([^.]+)/.exec(path)?.[1];
}

function readStructuredDetails(
  value: unknown,
  sanitiseLines: (values: Iterable<string | null | undefined>) => string[],
  serviceBlueprint?: AuthoredServiceBlueprint,
): Array<{ message: string; stageKey?: string }> {
  const entries = Array.isArray(value) ? value : typeof value === 'string' ? [value] : [];

  return entries.flatMap((entry): Array<{ message: string; stageKey?: string }> => {
    const rawMessage =
      entry && typeof entry === 'object' && 'message' in entry && typeof (entry as { message?: unknown }).message === 'string'
        ? (entry as { message: string }).message
        : typeof entry === 'string'
          ? entry
          : null;
    const [message] = sanitiseLines([rawMessage]);
    if (!message) {
      return [];
    }

    const rawStageKey =
      entry && typeof entry === 'object' && 'path' in entry
        ? stageKeyFromDiagnosticPath((entry as { path?: unknown }).path)
        : undefined;
    const stages = Array.isArray(serviceBlueprint?.stages) ? (serviceBlueprint!.stages as Array<{ stageKey: string; displayName: string }>) : [];
    const stage = rawStageKey ? stages.find((s) => s.stageKey === rawStageKey) : undefined;

    // Only offer a jump when the stage actually resolves — a dangling/renamed key isn't
    // navigable, and showing a dead "jump" affordance would be worse than showing none.
    return [{
      message: stage ? `${stage.displayName}: ${message}` : message,
      stageKey: stage ? rawStageKey : undefined,
    }];
  });
}

export class UmbracoWayfinderServiceBlueprintSource {
  private readonly getToken: () => Promise<string | undefined>;

  constructor(getToken: () => Promise<string | undefined>) {
    this.getToken = getToken;
  }

  private async authHeaders(extra: Record<string, string> = {}): Promise<Record<string, string>> {
    const token = await this.getToken();
    return {
      ...extra,
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    };
  }

  async list(): Promise<Array<{ blueprintKey: string; definitionKey: string; displayName: string }>> {
    const response = await fetch(API_BASE, {
      headers: await this.authHeaders({ Accept: 'application/json' }),
      credentials: 'same-origin',
    });
    if (!response.ok) {
      throw new Error(`Failed to list service blueprints (${response.status} ${response.statusText}).`);
    }
    const summaries = (await response.json()) as Array<{ definitionKey: string; displayName: string }>;
    return summaries.map(({ definitionKey, displayName }) => ({
      blueprintKey: definitionKey,
      definitionKey,
      displayName,
    }));
  }

  async load(blueprintKey: string): Promise<AuthoredServiceBlueprint> {
    const response = await fetch(`${API_BASE}/${encodeURIComponent(blueprintKey)}`, {
      headers: await this.authHeaders({ Accept: 'application/json' }),
      credentials: 'same-origin',
    });
    if (!response.ok) {
      throw new Error(`Failed to load service blueprint '${blueprintKey}' (${response.status} ${response.statusText}).`);
    }
    const payload = (await response.json()) as AuthoredServiceBlueprint;
    const { hydrateServiceBlueprintDefinition } = await loadWayfinderElements();
    return hydrateServiceBlueprintDefinition(payload);
  }

  async save(blueprintKey: string, serviceBlueprint: AuthoredServiceBlueprint): Promise<void> {
    const { serializeAuthoredServiceBlueprint } = await loadWayfinderElements();
    const body = serializeAuthoredServiceBlueprint(serviceBlueprint);
    const response = await fetch(`${API_BASE}/${encodeURIComponent(blueprintKey)}`, {
      method: 'PUT',
      headers: await this.authHeaders({ 'Content-Type': 'application/json', Accept: 'application/json' }),
      credentials: 'same-origin',
      body,
    });
    if (!response.ok) {
      throw await this.buildSaveError(response, blueprintKey, serviceBlueprint);
    }
  }

  async checkVersion(blueprintKey: string): Promise<number | null> {
    const response = await fetch(`${API_BASE}/${encodeURIComponent(blueprintKey)}/version`, {
      headers: await this.authHeaders({ Accept: 'application/json' }),
      credentials: 'same-origin',
    });
    if (!response.ok) {
      return null;
    }
    const payload = (await response.json()) as { version?: unknown };
    return typeof payload.version === 'number' ? payload.version : null;
  }

  private async buildSaveError(response: Response, blueprintKey: string, serviceBlueprint?: AuthoredServiceBlueprint): Promise<Error> {
    const { ServiceBlueprintSaveError, sanitiseServiceBlueprintSaveErrorLines, sanitiseServiceBlueprintSaveErrorText } =
      await loadWayfinderElements();
    const payloadText = await response.text().catch(() => '');
    const status = response.status;
    const statusText = response.statusText;
    const contentType = response.headers.get('content-type') ?? '';

    const fallbackSummary = sanitiseServiceBlueprintSaveErrorText(payloadText) ?? `Save failed (${status} ${statusText}).`;

    if (contentType.includes('json') || payloadText.trim().startsWith('{')) {
      try {
        const parsed = JSON.parse(payloadText) as ServiceBlueprintSaveOutcomePayload | ProblemDetailsPayload;

        if (status === 409) {
          const conflict = parsed as ServiceBlueprintSaveOutcomePayload;
          const currentVersion = typeof conflict.currentVersion === 'number' ? conflict.currentVersion : null;
          const detailLines = readStructuredDetails(conflict.diagnostics, sanitiseServiceBlueprintSaveErrorLines).map((d) => d.message);
          const summary =
            sanitiseServiceBlueprintSaveErrorText(detailLines[0]) ??
            `“${blueprintKey}” was changed elsewhere since you loaded it${currentVersion != null ? ` (now at version ${currentVersion})` : ''}.`;
          return new ServiceBlueprintSaveError({
            title: 'This service blueprint changed elsewhere',
            summary,
            detailLines: detailLines.filter((line) => line !== summary),
            statusCode: 409,
            isConflict: true,
            currentVersion,
          });
        }

        if (Array.isArray((parsed as ServiceBlueprintSaveOutcomePayload).diagnostics)) {
          const outcome = parsed as ServiceBlueprintSaveOutcomePayload;
          const details = readStructuredDetails(outcome.diagnostics, sanitiseServiceBlueprintSaveErrorLines, serviceBlueprint);
          const summary =
            sanitiseServiceBlueprintSaveErrorText(details[0]?.message) ??
            `“${blueprintKey}” has a problem that must be fixed before it can be saved.`;
          return new ServiceBlueprintSaveError({
            title: 'This service blueprint can’t be saved yet',
            summary,
            details: details.filter((d) => d.message !== summary),
            summaryStageKey: details[0]?.message === summary ? details[0]?.stageKey : undefined,
            statusCode: 400,
          });
        }

        const problem = parsed as ProblemDetailsPayload;
        const title = sanitiseServiceBlueprintSaveErrorText(typeof problem.title === 'string' ? problem.title : null) ?? 'We couldn’t save this service blueprint';
        const summary =
          sanitiseServiceBlueprintSaveErrorText(
            typeof problem.summary === 'string'
              ? problem.summary
              : typeof problem.detail === 'string'
                ? problem.detail
                : typeof problem.message === 'string'
                  ? problem.message
                  : null,
          ) ?? `The backoffice rejected the save request for “${blueprintKey}”.`;
        const detailLines = sanitiseServiceBlueprintSaveErrorLines([
          ...readStructuredDetails(problem.errors, sanitiseServiceBlueprintSaveErrorLines).map((d) => d.message),
          ...readStructuredDetails(problem.extensions?.errors, sanitiseServiceBlueprintSaveErrorLines).map((d) => d.message),
        ]).filter((line: string) => line !== summary);
        const traceId = sanitiseServiceBlueprintSaveErrorText(
          typeof problem.traceId === 'string' ? problem.traceId : typeof problem.extensions?.traceId === 'string' ? problem.extensions.traceId : null,
        );
        return new ServiceBlueprintSaveError({ title, summary, detailLines, traceId, statusCode: status });
      } catch {
        // Fall through to the plain-text fallback.
      }
    }

    return new ServiceBlueprintSaveError({
      title: 'We couldn’t save this service blueprint',
      summary: fallbackSummary,
      statusCode: status,
    });
  }
}
