import createClient, { type Client } from 'openapi-fetch';
import type { components, paths } from './schema';

/** Server-side API client. Never import this into a client-side script: it carries the user's bearer token. */
export type Api = Client<paths>;
export type Schemas = components['schemas'];

export interface ApiProblem {
  /** HTTP status; 503 when the API could not be reached at all. */
  status: number;
  title?: string;
  detail?: string;
  /** What to show the user: the problem's detail or title, or a sentence for the status. */
  message: string;
  /** Validation errors keyed by camelCase field path (`firstName`, `tees[0].holes[2].par`). */
  fieldErrors: Record<string, string[]>;
  /** Extra members of the problem body beyond the standard ones (the 409 `unmatched` list, `holeNumbers`). Absent when there are none. */
  extensions?: Record<string, unknown>;
}

export type ApiResult<T> = { ok: true; status: number; data: T } | { ok: false; problem: ApiProblem };

export function createApi(options: { baseUrl: string; accessToken?: string | undefined; fetch?: (request: Request) => Promise<Response> }): Api {
  const client = createClient<paths>({
    baseUrl: options.baseUrl.replace(/\/+$/, ''),
    ...(options.fetch ? { fetch: options.fetch } : {}),
  });
  const token = options.accessToken;
  if (token) {
    client.use({
      onRequest({ request }) {
        request.headers.set('Authorization', `Bearer ${token}`);
        return request;
      },
    });
  }
  return client;
}

const statusMessages: Record<number, string> = {
  400: 'Check what you entered and try again.',
  401: 'Your session has ended. Sign in again.',
  403: 'You don’t have access to that.',
  404: 'That wasn’t found.',
  409: 'That conflicts with something that already exists.',
  502: 'A service we rely on isn’t responding. Try again in a minute.',
  503: 'Can’t reach the Custom Golf Stats service. Try again in a minute.',
};

function messageForStatus(status: number): string {
  return statusMessages[status] ?? 'Something went wrong on our side. Try again in a minute.';
}

/** `FirstName` → `firstName`, `Tees[0].Holes[2].Par` → `tees[0].holes[2].par`. */
export function normalizeFieldKey(key: string): string {
  return key
    .split('.')
    .map((segment) => (segment.length > 0 ? segment[0]!.toLowerCase() + segment.slice(1) : segment))
    .join('.');
}

function text(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim().length > 0 ? value : undefined;
}

/** Maps an RFC 9457 problem details body (or anything else the API returned) to what pages display. */
export function toProblem(status: number, body: unknown): ApiProblem {
  const record = typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : {};
  const fieldErrors: Record<string, string[]> = {};
  const errors = record.errors;
  if (typeof errors === 'object' && errors !== null) {
    for (const [key, messages] of Object.entries(errors as Record<string, unknown>)) {
      if (!Array.isArray(messages)) continue;
      const strings = messages.filter((m): m is string => typeof m === 'string');
      if (strings.length > 0) fieldErrors[normalizeFieldKey(key)] = strings;
    }
  }
  const title = text(record.title);
  const detail = text(record.detail);
  const hasFieldErrors = Object.keys(fieldErrors).length > 0;
  const message = hasFieldErrors ? 'Check the highlighted fields.' : (detail ?? title ?? messageForStatus(status));
  const standard = new Set(['type', 'title', 'status', 'detail', 'instance', 'errors', 'traceId']);
  const extensions = Object.fromEntries(Object.entries(record).filter(([key]) => !standard.has(key)));
  return {
    status,
    ...(title !== undefined ? { title } : {}),
    ...(detail !== undefined ? { detail } : {}),
    message,
    fieldErrors,
    ...(Object.keys(extensions).length > 0 ? { extensions } : {}),
  };
}

/** All validation messages for a field, matched case-insensitively. */
export function fieldErrors(problem: ApiProblem | null | undefined, field: string): string[] {
  if (!problem) return [];
  const wanted = field.toLowerCase();
  for (const [key, messages] of Object.entries(problem.fieldErrors)) {
    if (key.toLowerCase() === wanted) return messages;
  }
  return [];
}

/** The first validation message for a field, matched case-insensitively. */
export function fieldError(problem: ApiProblem | null | undefined, field: string): string | undefined {
  return fieldErrors(problem, field)[0];
}

/** Awaits an openapi-fetch call and turns it into a result; never throws for HTTP or network failures. */
export async function call<T>(request: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<ApiResult<T>> {
  let result: { data?: T; error?: unknown; response: Response };
  try {
    result = await request;
  } catch {
    return { ok: false, problem: toProblem(503, undefined) };
  }
  const { response } = result;
  if (response.ok) return { ok: true, status: response.status, data: result.data as T };
  return { ok: false, problem: toProblem(response.status, result.error) };
}
