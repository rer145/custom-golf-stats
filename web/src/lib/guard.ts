import type { AstroCookies } from 'astro';
import type { ApiResult } from '../api/client';
import { clearSessionCookie } from './cookies';
import { signInRedirect } from './routes';

type RedirectStatus = 300 | 301 | 302 | 303 | 304 | 307 | 308;

/** The part of `Astro` / the middleware context that `endSession` needs. Pass `Astro` itself. */
export interface SessionContext {
  cookies: AstroCookies;
  url: URL;
  redirect: (path: string, status?: RedirectStatus) => Response;
}

/** True when an API call says the session's token is no longer accepted (401). */
export function isSessionEnded(result: ApiResult<unknown>): boolean {
  return !result.ok && result.problem.status === 401;
}

/**
 * The API refused the session (401): delete the cookie and send the user to sign in, returning here afterwards.
 *
 *   const r = await call(Astro.locals.api.GET('/me'));
 *   if (isSessionEnded(r)) return endSession(Astro);
 */
export function endSession(context: SessionContext): Response {
  clearSessionCookie(context.cookies);
  return context.redirect(signInRedirect(context.url), 303);
}
