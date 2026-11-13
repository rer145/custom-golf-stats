import { expect, test } from '@playwright/test';
import { PUBLIC_PATHS } from '../../src/lib/routes';
import { listPagePaths } from '../support/pages';
import { USER, signIn } from './support';

// Every page file under src/pages, enumerated from disk, so a new page can't be added unguarded.
const pages = listPagePaths();
const guarded = pages.filter((p) => !PUBLIC_PATHS.has(p.path));
const admin = guarded.filter((p) => p.path === '/admin' || p.path.startsWith('/admin/'));

test('the page list is not empty', () => {
  expect(guarded.length).toBeGreaterThanOrEqual(5);
  expect(admin.length).toBeGreaterThanOrEqual(1);
});

for (const { file, path } of guarded) {
  test(`signed out: ${file} (${path}) redirects to sign-in`, async ({ request }) => {
    for (const method of ['GET', 'POST'] as const) {
      const res = await request.fetch(path, { method, maxRedirects: 0, headers: { origin: 'http://127.0.0.1:4321' } });
      expect(res.status(), `${method} ${path}`).toBe(303);
      expect(res.headers()['location'], `${method} ${path}`).toBe(`/sign-in?next=${encodeURIComponent(path)}`);
    }
  });
}

for (const { file, path } of admin) {
  test(`non-admin: ${file} (${path}) is 403`, async ({ page }) => {
    await signIn(page, USER);
    const res = await page.request.get(path, { maxRedirects: 0 });
    expect(res.status()).toBe(403);
    expect(await res.text()).toContain('No access');
  });
}

for (const path of ['/', '/sign-in', '/sign-up']) {
  test(`public: ${path} renders signed out`, async ({ request }) => {
    const res = await request.get(path, { maxRedirects: 0 });
    expect(res.status()).toBe(200);
  });
}
