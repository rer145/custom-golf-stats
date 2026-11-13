import { describe, expect, it } from 'vitest';
import { listPagePaths, pageFileToPath } from '../../tests/support/pages';
import { PUBLIC_PATHS, routeAccess, safeNext, signInRedirect } from './routes';

describe('pageFileToPath', () => {
  it('maps page files to sample URLs', () => {
    expect(pageFileToPath('index.astro')).toBe('/');
    expect(pageFileToPath('sign-out.ts')).toBe('/sign-out');
    expect(pageFileToPath('stats/[year]/index.astro')).toBe('/stats/2025');
    expect(pageFileToPath('courses/[id]/edit.astro')).toBe('/courses/11111111-1111-4111-8111-111111111111/edit');
  });
});

describe('routeAccess', () => {
  it('public pages are exactly the landing, auth and error pages', () => {
    expect([...PUBLIC_PATHS].sort()).toEqual(['/', '/403', '/404', '/sign-in', '/sign-out', '/sign-up']);
    for (const p of PUBLIC_PATHS) expect(routeAccess(p)).toBe('public');
    expect(routeAccess('/sign-in/')).toBe('public');
  });

  it('admin routes need an admin', () => {
    expect(routeAccess('/admin')).toBe('admin');
    expect(routeAccess('/admin/')).toBe('admin');
    expect(routeAccess('/admin/users')).toBe('admin');
    expect(routeAccess('/administrator')).toBe('protected');
  });

  it('anything else is protected, including unknown paths', () => {
    expect(routeAccess('/stats')).toBe('protected');
    expect(routeAccess('/no-such-page')).toBe('protected');
    expect(routeAccess('/sign-in-elsewhere')).toBe('protected');
  });

  it('every page file under src/pages is guarded unless it is one of the public pages', () => {
    const pages = listPagePaths();
    expect(pages.length).toBeGreaterThan(PUBLIC_PATHS.size);
    for (const { file, path } of pages) {
      const access = routeAccess(path);
      if (PUBLIC_PATHS.has(path)) {
        expect(access, file).toBe('public');
      } else {
        expect(access, file).not.toBe('public');
        if (path.startsWith('/admin')) expect(access, file).toBe('admin');
      }
    }
  });
});

describe('safeNext / signInRedirect', () => {
  it('keeps same-site paths', () => {
    expect(safeNext('/stats/2025?tab=putting')).toBe('/stats/2025?tab=putting');
  });

  it('refuses anything that could leave the site', () => {
    for (const bad of ['https://evil.test', '//evil.test', '/\\evil.test', '\\\\evil.test', 'stats', '', null, undefined, '/sign-in', '/sign-out', '/a\nb']) {
      expect(safeNext(bad)).toBe('/stats');
    }
  });

  it('builds the sign-in URL with the page to return to', () => {
    expect(signInRedirect(new URL('http://x.test/rounds?year=2025'))).toBe('/sign-in?next=%2Frounds%3Fyear%3D2025');
    expect(signInRedirect(new URL('http://x.test/'))).toBe('/sign-in');
  });
});
