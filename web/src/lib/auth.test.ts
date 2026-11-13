import { describe, expect, it } from 'vitest';
import { refreshTokens, register, signIn } from './auth';

const now = Date.UTC(2026, 10, 12);
const tokens = { tokenType: 'Bearer', accessToken: 'acc', expiresIn: 3600, refreshToken: 'ref' };

type Handler = (req: Request) => Response | Promise<Response>;
function fakeFetch(routes: Record<string, Handler>, seen: Request[] = []) {
  return async (req: Request) => {
    seen.push(req.clone());
    const url = new URL(req.url);
    const handler = routes[`${req.method} ${url.pathname}`];
    return handler ? handler(req) : new Response(null, { status: 404 });
  };
}
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
const base = 'http://api.test';

describe('signIn', () => {
  it('logs in, reads the role, and builds a session', async () => {
    const seen: Request[] = [];
    const fetch = fakeFetch(
      {
        'POST /auth/login': () => json(200, tokens),
        'GET /me/entitlements': () => json(200, { tier: 'Paid', role: 'Admin', features: { shotByShotEntry: true }, trialShotRoundsRemaining: null }),
      },
      seen,
    );
    const r = await signIn({ baseUrl: base, fetch }, ' Golfer@Example.test ', 'pw', now);
    expect(r.ok).toBe(true);
    if (r.ok) expect(r.session).toMatchObject({ accessToken: 'acc', refreshToken: 'ref', accessExpiresAt: now + 3600_000, email: 'Golfer@Example.test', role: 'Admin' });
    expect(await seen[0]!.json()).toEqual({ email: 'Golfer@Example.test', password: 'pw' });
    expect(seen[1]!.headers.get('authorization')).toBe('Bearer acc');
  });

  it('requires both fields without calling the API', async () => {
    const seen: Request[] = [];
    const r = await signIn({ baseUrl: base, fetch: fakeFetch({}, seen) }, '', '', now);
    expect(seen).toHaveLength(0);
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.problem.fieldErrors).toEqual({ email: ['Enter your email address.'], password: ['Enter your password.'] });
  });

  it('wrong password: one message, no field hints about which was wrong', async () => {
    const r = await signIn({ baseUrl: base, fetch: fakeFetch({ 'POST /auth/login': () => json(401, { title: 'Unauthorized', status: 401, detail: 'Failed' }) }) }, 'a@b.test', 'x', now);
    expect(r.ok).toBe(false);
    if (!r.ok) {
      expect(r.problem.message).toBe('That email and password don’t match an account.');
      expect(r.problem.fieldErrors).toEqual({});
    }
  });

  it('locked out and not allowed get their own messages', async () => {
    const locked = await signIn({ baseUrl: base, fetch: fakeFetch({ 'POST /auth/login': () => json(401, { detail: 'LockedOut' }) }) }, 'a@b.test', 'x', now);
    const disabled = await signIn({ baseUrl: base, fetch: fakeFetch({ 'POST /auth/login': () => json(401, { detail: 'NotAllowed' }) }) }, 'a@b.test', 'x', now);
    expect(!locked.ok && locked.problem.message).toMatch(/Too many attempts/);
    expect(!disabled.ok && disabled.problem.message).toMatch(/can’t sign in/);
  });

  it('API down: a service message', async () => {
    const r = await signIn(
      {
        baseUrl: base,
        fetch: async () => {
          throw new TypeError('fetch failed');
        },
      },
      'a@b.test',
      'x',
      now,
    );
    expect(!r.ok && r.problem.status).toBe(503);
  });

  it('a login answer with a bad expiresIn is a failed sign-in with a message', async () => {
    for (const expiresIn of [0, -1, 'later']) {
      const r = await signIn(
        { baseUrl: base, fetch: fakeFetch({ 'POST /auth/login': () => json(200, { ...tokens, expiresIn }), 'GET /me/entitlements': () => json(200, {}) }) },
        'a@b.test',
        'x',
        now,
      );
      expect(r.ok).toBe(false);
      if (!r.ok) {
        expect(r.problem.status).toBe(502);
        expect(r.problem.message).toMatch(/unexpected answer/);
      }
    }
  });

  it('entitlements failing still signs in, as a User', async () => {
    const r = await signIn({ baseUrl: base, fetch: fakeFetch({ 'POST /auth/login': () => json(200, tokens), 'GET /me/entitlements': () => json(500, {}) }) }, 'a@b.test', 'x', now);
    expect(r.ok && r.session.role).toBe('User');
  });
});

describe('register', () => {
  it('maps Identity error codes onto the email and password fields', async () => {
    const fetch = fakeFetch({
      'POST /auth/register': () =>
        json(400, {
          title: 'One or more validation errors occurred.',
          errors: {
            DuplicateUserName: ["Username 'a@b.test' is already taken."],
            PasswordTooShort: ['Passwords must be at least 6 characters.'],
            PasswordRequiresDigit: ["Passwords must have at least one digit ('0'-'9')."],
          },
        }),
    });
    const r = await register({ baseUrl: base, fetch }, 'a@b.test', 'x');
    expect(r.ok).toBe(false);
    if (!r.ok) {
      expect(r.problem.fieldErrors).toEqual({
        email: ["Username 'a@b.test' is already taken."],
        password: ['Passwords must be at least 6 characters.', "Passwords must have at least one digit ('0'-'9')."],
      });
      expect(r.problem.message).toBe('Check the highlighted fields.');
    }
  });

  it('succeeds on 200 with an empty body', async () => {
    const r = await register({ baseUrl: base, fetch: fakeFetch({ 'POST /auth/register': () => new Response(null, { status: 200, headers: { 'content-length': '0' } }) }) }, 'a@b.test', 'Password1!');
    expect(r.ok).toBe(true);
  });
});

describe('refreshTokens', () => {
  it('ok: new tokens and the current role', async () => {
    const seen: Request[] = [];
    const fetch = fakeFetch(
      {
        'POST /auth/refresh': () => json(200, { ...tokens, accessToken: 'acc2' }),
        'GET /me/entitlements': () => json(200, { tier: 'Free', role: 'User', features: { shotByShotEntry: false }, trialShotRoundsRemaining: 3 }),
      },
      seen,
    );
    const r = await refreshTokens({ baseUrl: base, fetch }, 'ref');
    expect(r).toEqual({ kind: 'ok', tokens: { ...tokens, accessToken: 'acc2' }, role: 'User' });
    expect(await seen[0]!.json()).toEqual({ refreshToken: 'ref' });
    expect(seen[1]!.headers.get('authorization')).toBe('Bearer acc2');
  });

  it('only 400 and 401 are rejected; other statuses, bad answers and network failures are unavailable', async () => {
    const status = (s: number) => refreshTokens({ baseUrl: base, fetch: fakeFetch({ 'POST /auth/refresh': () => new Response(null, { status: s }) }) }, 'r');
    expect(await status(400)).toEqual({ kind: 'rejected' });
    expect(await status(401)).toEqual({ kind: 'rejected' });
    for (const s of [403, 404, 405, 408, 409, 429, 500, 502, 503]) expect(await status(s), String(s)).toEqual({ kind: 'unavailable' });
    expect(
      await refreshTokens({ baseUrl: base, fetch: fakeFetch({ 'POST /auth/refresh': () => json(200, { ...tokens, expiresIn: -1 }) }) }, 'r'),
    ).toEqual({ kind: 'unavailable' });
    expect(
      await refreshTokens(
        {
          baseUrl: base,
          fetch: async () => {
            throw new TypeError('down');
          },
        },
        'r',
      ),
    ).toEqual({ kind: 'unavailable' });
  });
});
