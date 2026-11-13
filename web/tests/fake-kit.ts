/**
 * Types and helpers shared by the fake API (tests/fake-api.ts) and its feature route modules (tests/fake-routes/*.ts).
 * Route modules import from here, never from fake-api.ts (that module starts the server).
 */
import type { components } from '../src/api/schema';

export type Schemas = components['schemas'];

export interface FakeUser {
  id: string;
  email: string;
  password: string;
  role: 'User' | 'Admin';
  tier: 'Free' | 'Paid';
  /** Access token lifetime in seconds (30 for short@example.test, so every request needs a refresh). */
  expiresIn: number;
  disabled?: boolean;
  firstName: string;
  lastName: string;
}

export interface RecordedCall {
  method: string;
  path: string;
  query: Record<string, string>;
  body: unknown;
  /** The user the bearer token belonged to, if any. */
  user: string | null;
  at: number;
}

export interface FakeRequest {
  method: string;
  path: string;
  params: Record<string, string>;
  query: URLSearchParams;
  body: unknown;
  /** The authenticated user (routes with `auth: true` always get one). */
  user: FakeUser | null;
  state: FakeState;
}

export interface FakeResponse {
  status: number;
  body?: unknown;
  headers?: Record<string, string>;
}

export interface FakeRoute {
  method: 'GET' | 'POST' | 'PUT' | 'DELETE';
  /** OpenAPI-style path, e.g. `/rounds/{id}`. */
  path: string;
  /** Require a valid bearer token (401 otherwise). Defaults to true. */
  auth?: boolean;
  handler: (req: FakeRequest) => FakeResponse | Promise<FakeResponse>;
}

export interface FakeState {
  users: FakeUser[];
  accessTokens: Map<string, { email: string; expiresAt: number }>;
  refreshTokens: Map<string, string>;
  calls: RecordedCall[];
  /** Free-form per-feature state for route modules (reset with the rest). */
  data: Map<string, unknown>;
}

/** An RFC 9457 problem; `errors` makes it an HttpValidationProblemDetails. */
export function problem(status: number, title: string, detail?: string, errors?: Record<string, string[]>): FakeResponse {
  return {
    status,
    headers: { 'content-type': 'application/problem+json' },
    body: { type: 'https://tools.ietf.org/html/rfc9110#section-15', title, status, ...(detail ? { detail } : {}), ...(errors ? { errors } : {}) },
  };
}

export const ok = (body: unknown): FakeResponse => ({ status: 200, body });

/** The user a route with `auth` (the default) runs for; throws if called from an anonymous route. */
export function requireUser(req: FakeRequest): FakeUser {
  if (!req.user) throw new Error(`${req.method} ${req.path} needs auth`);
  return req.user;
}
