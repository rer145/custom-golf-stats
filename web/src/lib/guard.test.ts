import { describe, expect, it } from 'vitest';
import type { AstroCookies } from 'astro';
import { endSession, isSessionEnded } from './guard';

function fakeContext(url: string) {
  const deleted: { name: string; options: unknown }[] = [];
  const cookies = { delete: (name: string, options: unknown) => deleted.push({ name, options }) } as unknown as AstroCookies;
  const redirects: { path: string; status?: number }[] = [];
  return {
    deleted,
    redirects,
    context: {
      cookies,
      url: new URL(url),
      redirect: (path: string, status?: 301 | 302 | 303 | 307 | 308 | 300 | 304) => {
        redirects.push(status === undefined ? { path } : { path, status });
        return new Response(null, { status: status ?? 302, headers: { location: path } });
      },
    },
  };
}

describe('endSession', () => {
  it('clears the session cookie and redirects to sign-in with the page to return to', () => {
    const { context, deleted, redirects } = fakeContext('http://x.test/rounds?year=2024');
    const res = endSession(context);
    expect(deleted.map((d) => d.name)).toEqual(['cgs_session']);
    expect(redirects).toEqual([{ path: '/sign-in?next=%2Frounds%3Fyear%3D2024', status: 303 }]);
    expect(res.status).toBe(303);
  });
});

describe('isSessionEnded', () => {
  it('is true only for a 401 problem', () => {
    expect(isSessionEnded({ ok: false, problem: { status: 401, message: '', fieldErrors: {} } })).toBe(true);
    expect(isSessionEnded({ ok: false, problem: { status: 403, message: '', fieldErrors: {} } })).toBe(false);
    expect(isSessionEnded({ ok: true, status: 200, data: {} })).toBe(false);
  });
});
