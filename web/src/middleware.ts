import { defineMiddleware } from 'astro:middleware';
import { call, createApi } from './api/client';
import { refreshTokens } from './lib/auth';
import { serverConfig } from './lib/config';
import { clearSessionCookie, writeSessionCookie } from './lib/cookies';
import { declaresOversizedBody } from './lib/limits';
import { routeAccess, signInRedirect } from './lib/routes';
import { SESSION_COOKIE, resolveSession } from './lib/session';

/**
 * Runs for every on-demand request:
 * 1. Reads the encrypted session cookie; a tampered, expired or unreadable cookie is deleted.
 * 2. Refreshes the access token when it has a minute or less to live; a refused refresh signs the user out.
 * 3. Redirects signed-out requests for anything but the public pages to `/sign-in?next=…`.
 * 4. For `/admin`, checks `role === 'Admin'` live with `/me/entitlements` and shows the 403 page otherwise.
 */
export const onRequest = defineMiddleware(async (context, next) => {
  if (declaresOversizedBody(context.request)) {
    return new Response('Request body too large.', { status: 413, headers: { 'content-type': 'text/plain; charset=utf-8' } });
  }
  const config = serverConfig();
  const now = Date.now();
  const target = { baseUrl: config.apiBaseUrl };

  const resolved = await resolveSession({
    cookie: context.cookies.get(SESSION_COOKIE)?.value,
    secret: config.sessionSecret,
    now,
    refresh: (refreshToken) => refreshTokens(target, refreshToken),
  });
  let session = resolved.session;
  if (resolved.cookie === 'clear') clearSessionCookie(context.cookies);
  if (resolved.cookie === 'set' && session) writeSessionCookie(context.cookies, session, config.sessionSecret, now);

  context.locals.session = session;
  context.locals.api = createApi({ baseUrl: config.apiBaseUrl, accessToken: session?.accessToken });

  const access = routeAccess(context.url.pathname);
  if (access !== 'public' && !session) {
    return context.redirect(signInRedirect(context.url), 303);
  }

  if (access === 'admin' && session) {
    const entitlements = await call(context.locals.api.GET('/me/entitlements'));
    if (!entitlements.ok && entitlements.problem.status === 401) {
      clearSessionCookie(context.cookies);
      context.locals.session = null;
      return context.redirect(signInRedirect(context.url), 303);
    }
    const role = entitlements.ok ? entitlements.data.role : null;
    if (role !== null && role !== session.role) {
      session = { ...session, role };
      context.locals.session = session;
      writeSessionCookie(context.cookies, session, config.sessionSecret, now);
    }
    if (role !== 'Admin') {
      return noStore(await next('/403'));
    }
  }

  const response = await next();
  return session ? noStore(response) : response;
});

/** Pages rendered for a signed-in user are personal: never cache them anywhere. */
function noStore(response: Response): Response {
  try {
    response.headers.set('Cache-Control', 'private, no-store');
    return response;
  } catch {
    const copy = new Response(response.body, response);
    copy.headers.set('Cache-Control', 'private, no-store');
    return copy;
  }
}
