import type { APIRoute } from 'astro';
import { clearSessionCookie } from '../lib/cookies';

/**
 * Signing out deletes the session cookie. The API's bearer tokens are stateless, so there is nothing to revoke there;
 * the cookie was the only copy. POST only (the header's sign-out button), so a link elsewhere can't sign anyone out.
 */
export const POST: APIRoute = ({ cookies, redirect }) => {
  clearSessionCookie(cookies);
  return redirect('/', 303);
};

export const GET: APIRoute = ({ redirect }) => redirect('/', 303);
