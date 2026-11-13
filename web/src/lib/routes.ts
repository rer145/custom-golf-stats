/** Pages anyone can open. Everything else needs a session; `/admin` and below also needs the Admin role. */
export const PUBLIC_PATHS: ReadonlySet<string> = new Set(['/', '/sign-in', '/sign-up', '/sign-out', '/403', '/404']);

export const DEFAULT_AFTER_SIGN_IN = '/stats';

export type RouteAccess = 'public' | 'protected' | 'admin';

function normalize(pathname: string): string {
  return pathname.length > 1 ? pathname.replace(/\/+$/, '') || '/' : pathname;
}

export function routeAccess(pathname: string): RouteAccess {
  const path = normalize(pathname);
  if (PUBLIC_PATHS.has(path)) return 'public';
  if (path === '/admin' || path.startsWith('/admin/')) return 'admin';
  return 'protected';
}

/** A same-site path to return to after signing in; anything else (absolute, protocol-relative, auth pages) goes to the default. */
export function safeNext(value: string | null | undefined): string {
  if (typeof value !== 'string' || value.length === 0 || value.length > 2048) return DEFAULT_AFTER_SIGN_IN;
  if (!value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) return DEFAULT_AFTER_SIGN_IN;
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f\\]/.test(value)) return DEFAULT_AFTER_SIGN_IN;
  const path = normalize(value.split(/[?#]/)[0] ?? '/');
  if (path === '/sign-in' || path === '/sign-up' || path === '/sign-out') return DEFAULT_AFTER_SIGN_IN;
  return value;
}

/** `/sign-in?next=<the page that was asked for>`. */
export function signInRedirect(url: URL): string {
  const target = url.pathname + url.search;
  return target === '/' ? '/sign-in' : `/sign-in?next=${encodeURIComponent(target)}`;
}
