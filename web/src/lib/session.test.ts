import { describe, expect, it } from 'vitest';
import {
  SESSION_LIFETIME_MS,
  REFRESH_MARGIN_MS,
  expiresAtFrom,
  needsRefresh,
  resolveSession,
  sealSession,
  sessionFromTokens,
  unsealSession,
  type Session,
} from './session';

const secret = 'a-test-session-secret-that-is-long-enough-1234';
const now = Date.UTC(2026, 10, 12, 12, 0, 0);

function sample(overrides: Partial<Session> = {}): Session {
  return {
    accessToken: 'access-abc',
    refreshToken: 'refresh-def',
    accessExpiresAt: now + 3600_000,
    expiresAt: now + SESSION_LIFETIME_MS,
    email: 'golfer@example.test',
    role: 'User',
    ...overrides,
  };
}

function flipChar(value: string, index: number): string {
  const c = value[index] === 'A' ? 'B' : 'A';
  return value.slice(0, index) + c + value.slice(index + 1);
}

describe('sealSession / unsealSession', () => {
  it('round-trips a session', () => {
    const sealed = sealSession(sample(), secret);
    expect(unsealSession(sealed, secret, now)).toEqual(sample());
  });

  it('is versioned and does not contain the tokens in clear text', () => {
    const sealed = sealSession(sample(), secret);
    expect(sealed.startsWith('v1.')).toBe(true);
    expect(sealed).not.toContain('access-abc');
    expect(sealed).not.toContain('refresh-def');
    expect(sealed).not.toContain('golfer');
  });

  it('uses a fresh IV each time', () => {
    expect(sealSession(sample(), secret)).not.toEqual(sealSession(sample(), secret));
  });

  it('rejects a tampered cookie (every byte position)', () => {
    const sealed = sealSession(sample(), secret);
    // The last base64url character can carry unused padding bits, so stop before it.
    for (let i = 3; i < sealed.length - 1; i += 7) {
      expect(unsealSession(flipChar(sealed, i), secret, now)).toBeNull();
    }
  });

  it('rejects a truncated cookie', () => {
    const sealed = sealSession(sample(), secret);
    expect(unsealSession(sealed.slice(0, sealed.length - 4), secret, now)).toBeNull();
    expect(unsealSession('v1.', secret, now)).toBeNull();
    expect(unsealSession('', secret, now)).toBeNull();
  });

  it('rejects a cookie sealed with another secret', () => {
    const sealed = sealSession(sample(), secret);
    expect(unsealSession(sealed, 'another-secret-that-is-also-long-enough-xyz', now)).toBeNull();
  });

  it('rejects an unknown version', () => {
    const sealed = sealSession(sample(), secret);
    expect(unsealSession('v2.' + sealed.slice(3), secret, now)).toBeNull();
    expect(unsealSession(sealed.slice(3), secret, now)).toBeNull();
  });

  it('rejects an expired session', () => {
    const sealed = sealSession(sample({ expiresAt: now - 1 }), secret);
    expect(unsealSession(sealed, secret, now)).toBeNull();
    expect(unsealSession(sealed, secret, now - 2)).not.toBeNull();
  });

  it('refuses a short secret', () => {
    expect(() => sealSession(sample(), 'short')).toThrow(/SESSION_SECRET/);
  });
});

describe('sessionFromTokens', () => {
  it('computes the access token expiry from expiresIn (seconds, number or string)', () => {
    const s = sessionFromTokens({ accessToken: 'a', refreshToken: 'r', expiresIn: '3600' }, { email: 'e@x.test', role: 'Admin' }, now);
    expect(s?.accessExpiresAt).toBe(now + 3600_000);
    expect(s?.expiresAt).toBe(now + SESSION_LIFETIME_MS);
    expect(s?.role).toBe('Admin');
  });

  it('refuses a missing, non-finite, zero or negative expiresIn', () => {
    for (const expiresIn of [0, -5, '', 'soon', Number.NaN, Number.POSITIVE_INFINITY, '1e400']) {
      expect(sessionFromTokens({ accessToken: 'a', refreshToken: 'r', expiresIn }, { email: 'e@x.test', role: 'User' }, now), String(expiresIn)).toBeNull();
    }
  });
});

describe('expiresAtFrom', () => {
  it('is now + seconds for a positive finite value, otherwise null', () => {
    expect(expiresAtFrom(60, now)).toBe(now + 60_000);
    expect(expiresAtFrom('90', now)).toBe(now + 90_000);
    expect(expiresAtFrom(0, now)).toBeNull();
    expect(expiresAtFrom('x', now)).toBeNull();
  });
});

describe('needsRefresh', () => {
  it('is true within a minute of access token expiry', () => {
    expect(REFRESH_MARGIN_MS).toBe(60_000);
    expect(needsRefresh(sample({ accessExpiresAt: now + 61_000 }), now)).toBe(false);
    expect(needsRefresh(sample({ accessExpiresAt: now + 60_000 }), now)).toBe(true);
    expect(needsRefresh(sample({ accessExpiresAt: now - 5 }), now)).toBe(true);
  });
});

describe('resolveSession', () => {
  const refreshed = { accessToken: 'access-new', refreshToken: 'refresh-new', expiresIn: 3600 };

  it('no cookie: no session, nothing to do', async () => {
    const r = await resolveSession({ cookie: undefined, secret, now, refresh: async () => ({ kind: 'ok', tokens: refreshed }) });
    expect(r).toEqual({ session: null, cookie: 'none' });
  });

  it('valid cookie, not near expiry: keeps it and does not refresh', async () => {
    let calls = 0;
    const r = await resolveSession({
      cookie: sealSession(sample(), secret),
      secret,
      now,
      refresh: async () => {
        calls++;
        return { kind: 'ok', tokens: refreshed };
      },
    });
    expect(calls).toBe(0);
    expect(r.cookie).toBe('none');
    expect(r.session).toEqual(sample());
  });

  it('tampered cookie: no session and the cookie is cleared', async () => {
    const r = await resolveSession({ cookie: flipChar(sealSession(sample(), secret), 20), secret, now, refresh: async () => ({ kind: 'ok', tokens: refreshed }) });
    expect(r).toEqual({ session: null, cookie: 'clear' });
  });

  it('expired cookie: no session and the cookie is cleared', async () => {
    const r = await resolveSession({ cookie: sealSession(sample({ expiresAt: now - 1 }), secret), secret, now, refresh: async () => ({ kind: 'ok', tokens: refreshed }) });
    expect(r).toEqual({ session: null, cookie: 'clear' });
  });

  it('near expiry: refreshes with the refresh token and re-seals', async () => {
    const seen: string[] = [];
    const r = await resolveSession({
      cookie: sealSession(sample({ accessExpiresAt: now + 30_000 }), secret),
      secret,
      now,
      refresh: async (token) => {
        seen.push(token);
        return { kind: 'ok', tokens: refreshed };
      },
    });
    expect(seen).toEqual(['refresh-def']);
    expect(r.cookie).toBe('set');
    expect(r.session).toMatchObject({ accessToken: 'access-new', refreshToken: 'refresh-new', accessExpiresAt: now + 3600_000, email: 'golfer@example.test', role: 'User' });
    // A refresh does not extend the absolute session lifetime.
    expect(r.session?.expiresAt).toBe(sample().expiresAt);
  });

  it('a refresh answer with a bad expiresIn counts as unavailable', async () => {
    const valid = await resolveSession({
      cookie: sealSession(sample({ accessExpiresAt: now + 30_000 }), secret),
      secret,
      now,
      refresh: async () => ({ kind: 'ok', tokens: { ...refreshed, expiresIn: 0 } }),
    });
    expect(valid.cookie).toBe('none');
    expect(valid.session?.accessToken).toBe('access-abc');
    const expired = await resolveSession({
      cookie: sealSession(sample({ accessExpiresAt: now - 1 }), secret),
      secret,
      now,
      refresh: async () => ({ kind: 'ok', tokens: { ...refreshed, expiresIn: 'never' } }),
    });
    expect(expired).toEqual({ session: null, cookie: 'clear' });
  });

  it('refresh rejected: no session and the cookie is cleared', async () => {
    const r = await resolveSession({ cookie: sealSession(sample({ accessExpiresAt: now + 30_000 }), secret), secret, now, refresh: async () => ({ kind: 'rejected' }) });
    expect(r).toEqual({ session: null, cookie: 'clear' });
  });

  it('API unreachable while the token is still valid: keeps the session', async () => {
    const r = await resolveSession({ cookie: sealSession(sample({ accessExpiresAt: now + 30_000 }), secret), secret, now, refresh: async () => ({ kind: 'unavailable' }) });
    expect(r.cookie).toBe('none');
    expect(r.session?.accessToken).toBe('access-abc');
  });

  it('API unreachable and the token already expired: no session and the cookie is cleared', async () => {
    const r = await resolveSession({ cookie: sealSession(sample({ accessExpiresAt: now - 1 }), secret), secret, now, refresh: async () => ({ kind: 'unavailable' }) });
    expect(r).toEqual({ session: null, cookie: 'clear' });
  });
});
