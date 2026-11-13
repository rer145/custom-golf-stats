import type { AstroCookies } from 'astro';
import { SESSION_COOKIE, sealSession, type Session } from './session';

/** Writes the encrypted session cookie: httpOnly, Secure, SameSite=Lax, living as long as the session. */
export function writeSessionCookie(cookies: AstroCookies, session: Session, secret: string, now: number): void {
  cookies.set(SESSION_COOKIE, sealSession(session, secret), {
    httpOnly: true,
    secure: true,
    sameSite: 'lax',
    path: '/',
    maxAge: Math.max(0, Math.floor((session.expiresAt - now) / 1000)),
  });
}

export function clearSessionCookie(cookies: AstroCookies): void {
  cookies.delete(SESSION_COOKIE, { httpOnly: true, secure: true, sameSite: 'lax', path: '/' });
}
