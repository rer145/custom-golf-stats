import { expect, test, type Response } from '@playwright/test';
import { sealSession } from '../../src/lib/session';
import { ADMIN, SESSION_COOKIE, SHORT, SHORT_REVOKED, TOKEN_PATTERN, USER, fakeCalls, revokeRefreshTokens, signIn } from './support';

// Same test-only value as playwright.config.ts, so tests can forge cookies the way an attacker can't.
const E2E_SECRET = 'e2e-only-session-secret-not-for-production-0123456789';

test('the session cookie is httpOnly, Secure, SameSite=Lax and opaque', async ({ page, context }) => {
  await signIn(page, USER);
  const cookie = (await context.cookies()).find((c) => c.name === SESSION_COOKIE);
  expect(cookie).toBeDefined();
  expect(cookie!.httpOnly).toBe(true);
  expect(cookie!.secure).toBe(true);
  expect(cookie!.sameSite).toBe('Lax');
  expect(cookie!.value).toMatch(/^v1\.[A-Za-z0-9_-]+$/);
  expect(cookie!.value).not.toMatch(TOKEN_PATTERN);
  // Scripts can't read it.
  expect(await page.evaluate(() => document.cookie)).not.toContain(SESSION_COOKIE);
  // Only the session cookie is set.
  expect((await context.cookies()).map((c) => c.name)).toEqual([SESSION_COOKIE]);
});

test('no token reaches the browser: HTML, scripts, styles, URLs, headers, storage', async ({ page }) => {
  const bodies: { url: string; text: string; headers: string }[] = [];
  const collect = async (res: Response) => {
    const type = res.headers()['content-type'] ?? '';
    const text = /html|javascript|css|json|text/.test(type) ? await res.text().catch(() => '') : '';
    bodies.push({ url: res.url(), text, headers: JSON.stringify(await res.allHeaders()) });
  };
  page.on('response', (res) => {
    void collect(res);
  });

  await signIn(page, ADMIN);
  for (const path of ['/stats', '/rounds', '/courses', '/profile', '/admin', '/']) {
    await page.goto(path);
    expect(await page.content(), path).not.toMatch(TOKEN_PATTERN);
  }
  await page.waitForLoadState('networkidle');

  // The pages really did call the API with this user's token (server side).
  expect((await fakeCalls(ADMIN)).length).toBeGreaterThan(0);

  expect(bodies.length).toBeGreaterThan(5);
  for (const b of bodies) {
    expect(b.url, 'url').not.toMatch(TOKEN_PATTERN);
    expect(b.text, b.url).not.toMatch(TOKEN_PATTERN);
    // Set-Cookie carries only the encrypted value.
    expect(b.headers, b.url).not.toMatch(TOKEN_PATTERN);
  }
  const storage = await page.evaluate(() => JSON.stringify({ ...localStorage }) + JSON.stringify({ ...sessionStorage }) + document.cookie);
  expect(storage).not.toMatch(TOKEN_PATTERN);
  // The browser never talks to the API directly.
  expect(bodies.some((b) => b.url.startsWith('http://127.0.0.1:4010'))).toBe(false);
});

test('an access token near expiry is refreshed and the cookie re-sealed', async ({ page, context }) => {
  await signIn(page, SHORT);
  const before = (await context.cookies()).find((c) => c.name === SESSION_COOKIE)!;

  const res = await page.goto('/rounds');
  expect(res?.status()).toBe(200);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Rounds');

  // Only a refresh re-seals the cookie.
  const after = (await context.cookies()).find((c) => c.name === SESSION_COOKIE)!.value;
  expect(after).not.toBe(before.value);
  const calls = await fakeCalls(SHORT);
  // The refresh was made with this user's refresh token (sign-in → /stats → /rounds: at least two refreshes)…
  expect(calls.filter((c) => c.path === '/auth/refresh').length).toBeGreaterThanOrEqual(2);
  // …and the refreshed access token works: entitlements were read as this user after it.
  const lastRefresh = calls.filter((c) => c.path === '/auth/refresh').at(-1)!;
  expect(calls.some((c) => c.path === '/me/entitlements' && c.at >= lastRefresh.at)).toBe(true);
});

test('a refused refresh signs the user out and clears the cookie', async ({ page, context }) => {
  await signIn(page, SHORT_REVOKED);
  await revokeRefreshTokens(SHORT_REVOKED);
  await page.goto('/rounds');
  await expect(page).toHaveURL('/sign-in?next=%2Frounds');
  expect((await context.cookies()).some((c) => c.name === SESSION_COOKIE)).toBe(false);
});

test('a tampered cookie is rejected and cleared', async ({ page, context, baseURL }) => {
  await signIn(page, USER);
  const cookie = (await context.cookies()).find((c) => c.name === SESSION_COOKIE)!;
  const i = 20;
  const tampered = cookie.value.slice(0, i) + (cookie.value[i] === 'A' ? 'B' : 'A') + cookie.value.slice(i + 1);
  await context.clearCookies();
  await context.addCookies([{ name: SESSION_COOKIE, value: tampered, url: baseURL!, httpOnly: true, secure: true, sameSite: 'Lax' }]);

  const res = await page.request.get('/stats', { maxRedirects: 0 });
  expect(res.status()).toBe(303);
  expect(res.headers()['location']).toBe('/sign-in?next=%2Fstats');
  expect(res.headers()['set-cookie']).toMatch(new RegExp(`^${SESSION_COOKIE}=deleted;`));
  expect(res.headers()['set-cookie']).toMatch(/Expires=Thu, 01 Jan 1970/i);
});

test('an expired session is rejected and cleared', async ({ page, context, baseURL }) => {
  const now = Date.now();
  const expired = sealSession(
    { accessToken: 'x', refreshToken: 'y', accessExpiresAt: now + 3600_000, expiresAt: now - 1000, email: USER, role: 'User' },
    E2E_SECRET,
  );
  await context.addCookies([{ name: SESSION_COOKIE, value: expired, url: baseURL!, httpOnly: true, secure: true, sameSite: 'Lax' }]);
  const res = await page.request.get('/profile', { maxRedirects: 0 });
  expect(res.status()).toBe(303);
  expect(res.headers()['location']).toBe('/sign-in?next=%2Fprofile');
  expect(res.headers()['set-cookie']).toMatch(new RegExp(`^${SESSION_COOKIE}=deleted;`));
});

test('signed-in pages are never cached', async ({ page }) => {
  await signIn(page, USER);
  const res = await page.request.get('/stats');
  expect(res.headers()['cache-control']).toBe('private, no-store');
});

test('cross-site form posts are refused', async ({ request }) => {
  const res = await request.post('/sign-in', {
    form: { email: USER, password: 'x' },
    headers: { origin: 'https://evil.example' },
    maxRedirects: 0,
  });
  expect(res.status()).toBe(403);
});

test('oversized request bodies are refused with 413', async ({ request }) => {
  const res = await request.post('/sign-in', {
    data: `email=a%40b.test&password=${'x'.repeat(1024 * 1024)}`,
    headers: { 'content-type': 'application/x-www-form-urlencoded', origin: 'http://127.0.0.1:4321' },
    maxRedirects: 0,
  });
  expect(res.status()).toBe(413);
});

test('a forged Host header does not steer redirects or pass the origin check', async ({ request }) => {
  const get = await request.get('/stats', { headers: { host: 'evil.example' }, maxRedirects: 0 });
  expect(get.status()).toBe(303);
  expect(get.headers()['location']).not.toContain('evil.example');
  const post = await request.post('/sign-in', {
    form: { email: USER, password: 'x' },
    headers: { host: 'evil.example', origin: 'http://evil.example' },
    maxRedirects: 0,
  });
  expect(post.status()).toBe(403);
});
