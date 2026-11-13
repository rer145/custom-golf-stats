/**
 * A small fake of the Custom Golf Stats API for end-to-end tests, so e2e needs no .NET or database.
 * Playwright starts it (see playwright.config.ts); run it by hand with `npm run fake-api`.
 *
 * Core (this file): Identity-style auth (/auth/login, /auth/register, /auth/refresh), bearer checks, /me,
 * /me/entitlements, and test controls under /__fake. Feature routes live in tests/fake-routes/*.ts: each module
 * exports `routes: FakeRoute[]` (types and helpers from tests/fake-kit.ts) and is loaded automatically, so parallel
 * tasks add files rather than edit this one.
 * Response bodies come from tests/fixtures/*.json, typed against the OpenAPI schema in the route modules.
 */
import { createServer, type IncomingMessage, type ServerResponse } from 'node:http';
import { randomUUID } from 'node:crypto';
import { readdirSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { ok, problem, requireUser, type FakeResponse, type FakeRoute, type FakeState, type FakeUser, type Schemas } from './fake-kit';
import usersFixture from './fixtures/users.json' with { type: 'json' };

function freshState(): FakeState {
  return {
    users: (usersFixture as FakeUser[]).map((u) => ({ ...u })),
    accessTokens: new Map(),
    refreshTokens: new Map(),
    calls: [],
    data: new Map(),
  };
}

function issueTokens(state: FakeState, user: FakeUser): Schemas['AccessTokenResponse'] {
  const accessToken = `fake-access-${randomUUID()}`;
  const refreshToken = `fake-refresh-${randomUUID()}`;
  state.accessTokens.set(accessToken, { email: user.email, expiresAt: Date.now() + user.expiresIn * 1000 });
  state.refreshTokens.set(refreshToken, user.email);
  return { tokenType: 'Bearer', accessToken, expiresIn: user.expiresIn, refreshToken };
}

const coreRoutes: FakeRoute[] = [
  {
    method: 'POST',
    path: '/auth/login',
    auth: false,
    handler: ({ body, state }) => {
      const { email, password } = (body ?? {}) as { email?: string; password?: string };
      const user = state.users.find((u) => u.email.toLowerCase() === String(email ?? '').toLowerCase());
      if (!user || user.password !== password) return problem(401, 'Unauthorized', 'Failed');
      if (user.disabled) return problem(401, 'Unauthorized', 'NotAllowed');
      return ok(issueTokens(state, user));
    },
  },
  {
    method: 'POST',
    path: '/auth/register',
    auth: false,
    handler: ({ body, state }) => {
      const { email = '', password = '' } = (body ?? {}) as { email?: string; password?: string };
      const errors: Record<string, string[]> = {};
      if (!/^[^@\s]+@[^@\s]+$/.test(email)) errors.InvalidEmail = [`Email '${email}' is invalid.`];
      else if (state.users.some((u) => u.email.toLowerCase() === email.toLowerCase())) errors.DuplicateUserName = [`Username '${email}' is already taken.`];
      if (password.length < 6) errors.PasswordTooShort = ['Passwords must be at least 6 characters.'];
      if (!/[0-9]/.test(password)) errors.PasswordRequiresDigit = ["Passwords must have at least one digit ('0'-'9')."];
      if (Object.keys(errors).length > 0) return problem(400, 'One or more validation errors occurred.', undefined, errors);
      state.users.push({ id: randomUUID(), email, password, role: 'User', tier: 'Free', expiresIn: 3600, firstName: '', lastName: '' });
      return { status: 200 };
    },
  },
  {
    method: 'POST',
    path: '/auth/refresh',
    auth: false,
    handler: ({ body, state }) => {
      const { refreshToken } = (body ?? {}) as { refreshToken?: string };
      const email = refreshToken ? state.refreshTokens.get(refreshToken) : undefined;
      const user = email ? state.users.find((u) => u.email === email) : undefined;
      if (!refreshToken || !user || user.disabled) return { status: 401 };
      // Like Identity, the old refresh token stays valid until it expires (no rotation), so concurrent refreshes are safe.
      return ok(issueTokens(state, user));
    },
  },
  {
    method: 'GET',
    path: '/me',
    handler: (req) => {
      const u = requireUser(req);
      const profile: Schemas['ProfileResponse'] = {
        id: u.id,
        email: u.email,
        firstName: u.firstName,
        lastName: u.lastName,
        gender: 'Male',
        ghinNumber: null,
        homeCourseId: null,
        units: 'Yards',
      };
      return ok(profile);
    },
  },
  {
    method: 'GET',
    path: '/me/entitlements',
    handler: (req) => {
      const u = requireUser(req);
      const entitlements: Schemas['EntitlementsResponse'] = {
        tier: u.tier,
        role: u.role,
        features: { shotByShotEntry: u.tier === 'Paid' },
        trialShotRoundsRemaining: u.tier === 'Paid' ? null : 3,
      };
      return ok(entitlements);
    },
  },
];

interface CompiledRoute extends FakeRoute {
  regex: RegExp;
  names: string[];
}

function compile(route: FakeRoute): CompiledRoute {
  const names: string[] = [];
  const pattern = route.path.replace(/[.*+?^$()|[\]\\]/g, '\\$&').replace(/\{([A-Za-z0-9_]+)\}/g, (_m, name: string) => {
    names.push(name);
    return '([^/]+)';
  });
  return { ...route, regex: new RegExp(`^${pattern}$`), names };
}

async function loadFeatureRoutes(): Promise<FakeRoute[]> {
  const dir = fileURLToPath(new URL('./fake-routes/', import.meta.url));
  const files = readdirSync(dir).filter((f) => /\.ts$/.test(f) && !f.endsWith('.d.ts')).sort();
  const routes: FakeRoute[] = [];
  for (const f of files) {
    const mod = (await import(pathToFileURL(`${dir}${f}`).href)) as { routes?: FakeRoute[] };
    routes.push(...(mod.routes ?? []));
  }
  return routes;
}

async function readBody(req: IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = [];
  for await (const chunk of req) chunks.push(chunk as Buffer);
  const text = Buffer.concat(chunks).toString('utf8');
  if (!text) return undefined;
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

function send(res: ServerResponse, r: FakeResponse): void {
  const headers: Record<string, string> = { ...(r.headers ?? {}) };
  if (r.body === undefined) {
    headers['content-length'] = '0';
    res.writeHead(r.status, headers).end();
    return;
  }
  const text = JSON.stringify(r.body);
  headers['content-type'] ??= 'application/json';
  res.writeHead(r.status, headers).end(text);
}

export async function startFakeApi(port: number, host = '127.0.0.1') {
  let state = freshState();
  const routes = [...coreRoutes, ...(await loadFeatureRoutes())].map(compile);

  const server = createServer(async (req, res) => {
    try {
      const url = new URL(req.url ?? '/', `http://${host}:${port}`);
      const method = req.method ?? 'GET';
      const body = await readBody(req);

      // ---- Test controls ----
      if (url.pathname === '/__fake/health') return send(res, ok({ ok: true }));
      if (url.pathname === '/__fake/reset' && method === 'POST') {
        state = freshState();
        return send(res, ok({ ok: true }));
      }
      if (url.pathname === '/__fake/calls' && method === 'GET') {
        const user = url.searchParams.get('user');
        return send(res, ok(state.calls.filter((c) => !user || c.user === user)));
      }
      if (url.pathname === '/__fake/revoke' && method === 'POST') {
        // Revokes one user's refresh tokens, e.g. to test a refused refresh.
        const email = String((body as { email?: string } | undefined)?.email ?? '');
        for (const [token, owner] of state.refreshTokens) if (owner === email) state.refreshTokens.delete(token);
        return send(res, ok({ ok: true }));
      }
      if (url.pathname === '/__fake/expire-access' && method === 'POST') {
        // Invalidates one user's access tokens (refresh tokens stay), so the next page-level API call gets a 401.
        const email = String((body as { email?: string } | undefined)?.email ?? '');
        for (const [token, owner] of state.accessTokens) if (owner.email === email) state.accessTokens.delete(token);
        return send(res, ok({ ok: true }));
      }

      const auth = req.headers.authorization ?? '';
      const token = auth.startsWith('Bearer ') ? auth.slice(7) : null;
      const session = token ? state.accessTokens.get(token) : undefined;
      const user = session && session.expiresAt > Date.now() ? (state.users.find((u) => u.email === session.email) ?? null) : null;

      // Auth calls carry no bearer token: record them against the account they name (login/register email, refresh token owner).
      const named = body as { email?: unknown; refreshToken?: unknown } | undefined;
      const authUser =
        typeof named?.refreshToken === 'string' ? (state.refreshTokens.get(named.refreshToken) ?? null) : typeof named?.email === 'string' ? named.email : null;
      state.calls.push({ method, path: url.pathname, query: Object.fromEntries(url.searchParams), body, user: user?.email ?? authUser, at: Date.now() });

      for (const route of routes) {
        if (route.method !== method) continue;
        const match = route.regex.exec(url.pathname);
        if (!match) continue;
        if (route.auth !== false && !user) return send(res, { status: 401 });
        const params = Object.fromEntries(route.names.map((n, i) => [n, decodeURIComponent(match[i + 1] ?? '')]));
        return send(res, await route.handler({ method, path: url.pathname, params, query: url.searchParams, body, user, state }));
      }
      return send(res, problem(404, 'Not Found'));
    } catch (err) {
      return send(res, problem(500, 'Fake API error', String(err)));
    }
  });

  await new Promise<void>((resolve) => server.listen(port, host, resolve));
  return server;
}

// Run directly (`tsx tests/fake-api.ts`): listen on FAKE_API_PORT.
if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  const port = Number(process.env.FAKE_API_PORT ?? 4010);
  await startFakeApi(port);
  console.log(`fake API listening on http://127.0.0.1:${port}`);
}
