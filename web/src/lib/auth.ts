import { call, createApi, toProblem, type ApiProblem } from '../api/client';
import { expiresAtFrom, sessionFromTokens, type RefreshOutcome, type Session } from './session';

export interface ApiTarget {
  baseUrl: string;
  fetch?: (request: Request) => Promise<Response>;
}

function api(target: ApiTarget, accessToken?: string) {
  return createApi({ baseUrl: target.baseUrl, accessToken, ...(target.fetch ? { fetch: target.fetch } : {}) });
}

function invalid(fieldErrors: Record<string, string[]>): ApiProblem {
  return { status: 400, message: 'Check the highlighted fields.', fieldErrors };
}

function requireCredentials(email: string, password: string): ApiProblem | null {
  const errors: Record<string, string[]> = {};
  if (!email) errors.email = ['Enter your email address.'];
  if (!password) errors.password = ['Enter your password.'];
  return Object.keys(errors).length > 0 ? invalid(errors) : null;
}

async function currentRole(target: ApiTarget, accessToken: string): Promise<string | undefined> {
  const r = await call(api(target, accessToken).GET('/me/entitlements'));
  return r.ok ? r.data.role : undefined;
}

const signInMessages: Record<string, string> = {
  Failed: 'That email and password don’t match an account.',
  LockedOut: 'Too many attempts. Wait a few minutes and try again.',
  NotAllowed: 'This account can’t sign in right now.',
  RequiresTwoFactor: 'This account needs a two-factor code, which the web site doesn’t support yet.',
};

export type SignInResult = { ok: true; session: Session } | { ok: false; problem: ApiProblem };

/** `/auth/login`, then `/me/entitlements` for the role shown in navigation. */
export async function signIn(target: ApiTarget, rawEmail: string, password: string, now: number): Promise<SignInResult> {
  const email = rawEmail.trim();
  const missing = requireCredentials(email, password);
  if (missing) return { ok: false, problem: missing };

  const login = await call(api(target).POST('/auth/login', { body: { email, password } }));
  if (!login.ok) {
    const { problem } = login;
    if (problem.status === 401) {
      const message = signInMessages[problem.detail ?? 'Failed'] ?? signInMessages.Failed!;
      return { ok: false, problem: { ...problem, message, fieldErrors: {} } };
    }
    return { ok: false, problem };
  }
  if (expiresAtFrom(login.data.expiresIn, now) === null) return { ok: false, problem: badAnswer() };
  const role = (await currentRole(target, login.data.accessToken)) ?? 'User';
  const session = sessionFromTokens(login.data, { email, role }, now);
  return session ? { ok: true, session } : { ok: false, problem: badAnswer() };
}

function badAnswer(): ApiProblem {
  return { status: 502, message: 'Sign-in got an unexpected answer from the server. Try again in a minute.', fieldErrors: {} };
}

/** Identity's register errors are keyed by error code (`DuplicateUserName`, `PasswordTooShort`…); put them on the form's fields. */
function identityField(code: string): 'email' | 'password' | null {
  const c = code.toLowerCase();
  if (c.includes('password')) return 'password';
  if (c.includes('email') || c.includes('username')) return 'email';
  return null;
}

export type RegisterResult = { ok: true } | { ok: false; problem: ApiProblem };

export async function register(target: ApiTarget, rawEmail: string, password: string): Promise<RegisterResult> {
  const email = rawEmail.trim();
  const missing = requireCredentials(email, password);
  if (missing) return { ok: false, problem: missing };

  const r = await call(api(target).POST('/auth/register', { body: { email, password } }));
  if (r.ok) return { ok: true };
  const { problem } = r;
  if (problem.status !== 400) return { ok: false, problem };

  const fieldErrors: Record<string, string[]> = {};
  const general: string[] = [];
  for (const [code, messages] of Object.entries(problem.fieldErrors)) {
    const field = identityField(code);
    if (field) (fieldErrors[field] ??= []).push(...messages);
    else general.push(...messages);
  }
  if (Object.keys(fieldErrors).length === 0 && general.length === 0) return { ok: false, problem };
  const mapped = toProblem(400, { title: problem.title, detail: general.join(' ') || undefined, errors: fieldErrors });
  return { ok: false, problem: general.length > 0 && Object.keys(fieldErrors).length === 0 ? { ...mapped, message: general.join(' ') } : mapped };
}

/**
 * `/auth/refresh` for the middleware. Only 400 and 401 mean the refresh token is no good (sign out); any other failure,
 * or an answer with an unusable `expiresIn`, is treated as temporary.
 */
export async function refreshTokens(target: ApiTarget, refreshToken: string): Promise<RefreshOutcome> {
  const r = await call(api(target).POST('/auth/refresh', { body: { refreshToken } }));
  if (!r.ok) {
    const s = r.problem.status;
    return s === 400 || s === 401 ? { kind: 'rejected' } : { kind: 'unavailable' };
  }
  if (expiresAtFrom(r.data.expiresIn, Date.now()) === null) return { kind: 'unavailable' };
  const role = await currentRole(target, r.data.accessToken);
  return { kind: 'ok', tokens: r.data, ...(role ? { role } : {}) };
}
