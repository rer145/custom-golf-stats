import { describe, expect, it } from 'vitest';
import { call, createApi, fieldError, toProblem } from './client';

function jsonResponse(status: number, body: unknown, contentType = 'application/json'): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { 'content-type': contentType } });
}

describe('toProblem', () => {
  it('maps a validation problem to field errors keyed by camelCase field path', () => {
    const p = toProblem(400, {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { FirstName: ['First name is required.'], 'Tees[0].Holes[2].Par': ['Par must be 3 to 6.', 'second'] },
    });
    expect(p.status).toBe(400);
    expect(p.fieldErrors).toEqual({ firstName: ['First name is required.'], 'tees[0].holes[2].par': ['Par must be 3 to 6.', 'second'] });
    expect(p.message).toBe('Check the highlighted fields.');
    expect(fieldError(p, 'firstName')).toBe('First name is required.');
    expect(fieldError(p, 'FIRSTNAME')).toBe('First name is required.');
    expect(fieldError(p, 'lastName')).toBeUndefined();
  });

  it('uses the detail of a 409 problem as the message', () => {
    const p = toProblem(409, { title: 'Conflict', status: 409, detail: 'This course was already imported.' });
    expect(p).toMatchObject({ status: 409, title: 'Conflict', detail: 'This course was already imported.', message: 'This course was already imported.', fieldErrors: {} });
  });

  it('falls back to the title, then to a message for the status', () => {
    expect(toProblem(502, { title: 'Bad Gateway', status: 502 }).message).toBe('Bad Gateway');
    expect(toProblem(502, undefined).message).toMatch(/isn.t responding/);
    expect(toProblem(404, '').message).toBe('That wasn’t found.');
    expect(toProblem(403, null).message).toMatch(/access/);
    expect(toProblem(500, 'Internal error text').message).toMatch(/went wrong/);
  });

  it('a 400 without field errors shows its detail', () => {
    expect(toProblem(400, { title: 'Bad Request', detail: 'Year is out of range.' }).message).toBe('Year is out of range.');
  });

  it('ignores malformed error collections', () => {
    const p = toProblem(400, { errors: { email: 'not-an-array', password: [1, 'Too short.'] } });
    expect(p.fieldErrors).toEqual({ password: ['Too short.'] });
  });
});

describe('createApi + call', () => {
  it('sends the bearer token to the base URL and returns data', async () => {
    const seen: Request[] = [];
    const api = createApi({
      baseUrl: 'http://api.test',
      accessToken: 'tok-123',
      fetch: async (req: Request) => {
        seen.push(req);
        return jsonResponse(200, { tier: 'Free', role: 'User', features: { shotByShotEntry: false }, trialShotRoundsRemaining: 3 });
      },
    });
    const r = await call(api.GET('/me/entitlements'));
    expect(r).toEqual({ ok: true, status: 200, data: { tier: 'Free', role: 'User', features: { shotByShotEntry: false }, trialShotRoundsRemaining: 3 } });
    expect(seen[0]?.url).toBe('http://api.test/me/entitlements');
    expect(seen[0]?.headers.get('authorization')).toBe('Bearer tok-123');
  });

  it('sends no authorization header without a token', async () => {
    const seen: Request[] = [];
    const api = createApi({ baseUrl: 'http://api.test/', fetch: async (req: Request) => (seen.push(req), new Response(null, { status: 200 })) });
    const r = await call(api.POST('/auth/register', { body: { email: 'a@b.test', password: 'x' } }));
    expect(r.ok).toBe(true);
    expect(seen[0]?.url).toBe('http://api.test/auth/register');
    expect(seen[0]?.headers.has('authorization')).toBe(false);
  });

  it('maps an error response to a problem', async () => {
    const api = createApi({ baseUrl: 'http://api.test', fetch: async () => jsonResponse(400, { errors: { Email: ['Taken.'] } }, 'application/problem+json') });
    const r = await call(api.POST('/auth/register', { body: { email: 'a@b.test', password: 'x' } }));
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.problem.fieldErrors).toEqual({ email: ['Taken.'] });
  });

  it('maps a network failure to a 503 problem without throwing', async () => {
    const api = createApi({
      baseUrl: 'http://api.test',
      fetch: async () => {
        throw new TypeError('fetch failed');
      },
    });
    const r = await call(api.GET('/stats/years'));
    expect(r.ok).toBe(false);
    if (!r.ok) {
      expect(r.problem.status).toBe(503);
      expect(r.problem.message).toMatch(/Can.t reach/);
    }
  });
});
