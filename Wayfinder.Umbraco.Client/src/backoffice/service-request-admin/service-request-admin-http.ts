// Shared bearer-token fetch helper for the service request admin screen's own data source — same
// shape as service-blueprint-http.ts's serviceBlueprintFetch, against this feature's own API base.

import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';

export const SERVICE_REQUEST_ADMIN_API_BASE = '/umbraco/management/api/v1/wayfinder/service-requests';

async function authHeaders(host: UmbControllerHost, extra: Record<string, string> = {}): Promise<Record<string, string>> {
  const authContext = await (host as UmbClassInterface).getContext(UMB_AUTH_CONTEXT);
  const token = await authContext?.getLatestToken();
  return {
    ...extra,
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
  };
}

export async function serviceRequestAdminFetch(host: UmbControllerHost, path: string, init: RequestInit = {}): Promise<Response> {
  return fetch(`${SERVICE_REQUEST_ADMIN_API_BASE}${path}`, {
    ...init,
    headers: {
      Accept: 'application/json',
      ...(await authHeaders(host, (init.headers as Record<string, string>) ?? {})),
    },
    credentials: 'same-origin',
  });
}
