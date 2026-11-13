import { createCipheriv, createDecipheriv, hkdfSync, randomBytes } from 'node:crypto';

/**
 * The signed-in session, kept only in an encrypted, httpOnly cookie. Tokens never leave the server in clear text:
 * pages read `Astro.locals.session` on the server and talk to the API with it.
 */
export interface Session {
  accessToken: string;
  refreshToken: string;
  /** Epoch ms when the access token expires (from the API's `expiresIn`). */
  accessExpiresAt: number;
  /** Epoch ms when the session itself ends, whatever the refreshes (matches the API's refresh token lifetime). */
  expiresAt: number;
  email: string;
  /** `role` from `/me/entitlements` at sign-in or the last refresh. Navigation only: admin pages re-check it live. */
  role: string;
}

export interface TokenResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number | string;
}

/** ASP.NET Identity's default bearer refresh token lifetime (14 days). */
export const SESSION_LIFETIME_MS = 14 * 24 * 60 * 60 * 1000;
/** Refresh the access token when it has a minute or less to live. */
export const REFRESH_MARGIN_MS = 60_000;
export const SESSION_COOKIE = 'cgs_session';
export const MIN_SECRET_LENGTH = 32;

const VERSION = 'v1';
const AAD = Buffer.from('custom-golf-stats.session.v1');
const IV_BYTES = 12;
const TAG_BYTES = 16;

const keyCache = new Map<string, Buffer>();

function keyFor(secret: string): Buffer {
  if (typeof secret !== 'string' || secret.length < MIN_SECRET_LENGTH) {
    throw new Error(`SESSION_SECRET must be at least ${MIN_SECRET_LENGTH} characters.`);
  }
  let key = keyCache.get(secret);
  if (!key) {
    key = Buffer.from(hkdfSync('sha256', secret, 'custom-golf-stats', 'session-cookie-aes-256-gcm-v1', 32));
    keyCache.set(secret, key);
  }
  return key;
}

/** Encrypts a session with AES-256-GCM: `v1.` + base64url(iv | ciphertext | tag). */
export function sealSession(session: Session, secret: string): string {
  const key = keyFor(secret);
  const iv = randomBytes(IV_BYTES);
  const cipher = createCipheriv('aes-256-gcm', key, iv);
  cipher.setAAD(AAD);
  const body = Buffer.concat([cipher.update(JSON.stringify(session), 'utf8'), cipher.final()]);
  return `${VERSION}.${Buffer.concat([iv, body, cipher.getAuthTag()]).toString('base64url')}`;
}

/** Decrypts and validates a cookie value; `null` when it is missing, malformed, tampered with, of another version, or expired. */
export function unsealSession(value: string | undefined, secret: string, now: number): Session | null {
  const key = keyFor(secret);
  if (!value || !value.startsWith(`${VERSION}.`)) return null;
  const encoded = value.slice(VERSION.length + 1);
  if (!/^[A-Za-z0-9_-]+$/.test(encoded)) return null;
  const raw = Buffer.from(encoded, 'base64url');
  if (raw.length <= IV_BYTES + TAG_BYTES) return null;
  try {
    const decipher = createDecipheriv('aes-256-gcm', key, raw.subarray(0, IV_BYTES));
    decipher.setAAD(AAD);
    decipher.setAuthTag(raw.subarray(raw.length - TAG_BYTES));
    const json = Buffer.concat([decipher.update(raw.subarray(IV_BYTES, raw.length - TAG_BYTES)), decipher.final()]).toString('utf8');
    const session = parseSession(JSON.parse(json));
    if (!session || session.expiresAt <= now) return null;
    return session;
  } catch {
    return null;
  }
}

function parseSession(value: unknown): Session | null {
  if (typeof value !== 'object' || value === null) return null;
  const v = value as Record<string, unknown>;
  if (
    typeof v.accessToken !== 'string' ||
    typeof v.refreshToken !== 'string' ||
    typeof v.accessExpiresAt !== 'number' ||
    typeof v.expiresAt !== 'number' ||
    typeof v.email !== 'string' ||
    typeof v.role !== 'string'
  ) {
    return null;
  }
  return {
    accessToken: v.accessToken,
    refreshToken: v.refreshToken,
    accessExpiresAt: v.accessExpiresAt,
    expiresAt: v.expiresAt,
    email: v.email,
    role: v.role,
  };
}

export function sessionFromTokens(tokens: TokenResponse, who: { email: string; role: string }, now: number): Session {
  return {
    accessToken: tokens.accessToken,
    refreshToken: tokens.refreshToken,
    accessExpiresAt: now + Number(tokens.expiresIn) * 1000,
    expiresAt: now + SESSION_LIFETIME_MS,
    email: who.email,
    role: who.role,
  };
}

export function needsRefresh(session: Session, now: number): boolean {
  return session.accessExpiresAt - now <= REFRESH_MARGIN_MS;
}

export type RefreshOutcome =
  | { kind: 'ok'; tokens: TokenResponse; role?: string }
  /** The API refused the refresh token (revoked, expired, account disabled). */
  | { kind: 'rejected' }
  /** The API could not be reached or failed; the session may still be usable. */
  | { kind: 'unavailable' };

export interface ResolvedSession {
  session: Session | null;
  /** What the caller must do with the cookie: leave it, write the re-sealed session, or delete it. */
  cookie: 'none' | 'set' | 'clear';
}

/** Reads the cookie, refreshing the access token when it is about to expire. */
export async function resolveSession(args: {
  cookie: string | undefined;
  secret: string;
  now: number;
  refresh: (refreshToken: string) => Promise<RefreshOutcome>;
}): Promise<ResolvedSession> {
  const { cookie, secret, now } = args;
  if (!cookie) return { session: null, cookie: 'none' };
  const session = unsealSession(cookie, secret, now);
  if (!session) return { session: null, cookie: 'clear' };
  if (!needsRefresh(session, now)) return { session, cookie: 'none' };

  const outcome = await args.refresh(session.refreshToken);
  switch (outcome.kind) {
    case 'ok':
      return {
        session: {
          ...session,
          accessToken: outcome.tokens.accessToken,
          refreshToken: outcome.tokens.refreshToken,
          accessExpiresAt: now + Number(outcome.tokens.expiresIn) * 1000,
          role: outcome.role ?? session.role,
        },
        cookie: 'set',
      };
    case 'rejected':
      return { session: null, cookie: 'clear' };
    case 'unavailable':
      return session.accessExpiresAt > now ? { session, cookie: 'none' } : { session: null, cookie: 'clear' };
  }
}
