import { expect, type Page } from '@playwright/test';
import type { RecordedCall } from '../fake-kit';

/** Test-only credentials from tests/fixtures/users.json (the fake API's users). */
export const PASSWORD = 'Fairway-123';
export const USER = 'user@example.test';
export const ADMIN = 'admin@example.test';
/** Access tokens live 30 s, so every request refreshes. */
export const SHORT = 'short@example.test';
/** Also 30 s tokens; a separate user so revoking its refresh tokens can't race the other refresh tests. */
export const SHORT_REVOKED = 'short-revoked@example.test';
export const DISABLED = 'disabled@example.test';

export const SESSION_COOKIE = 'cgs_session';
export const FAKE_API_URL = `http://127.0.0.1:${process.env.FAKE_API_PORT ?? 4010}`;

export async function signIn(page: Page, email: string, password = PASSWORD, next?: string): Promise<void> {
  await page.goto(next ? `/sign-in?next=${encodeURIComponent(next)}` : '/sign-in');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).not.toHaveURL(/\/sign-in/);
}

/** API calls the fake recorded for one user (by bearer token owner, or the email in an auth body). */
export async function fakeCalls(email?: string): Promise<RecordedCall[]> {
  const res = await fetch(`${FAKE_API_URL}/__fake/calls${email ? `?user=${encodeURIComponent(email)}` : ''}`);
  return (await res.json()) as RecordedCall[];
}

export async function revokeRefreshTokens(email: string): Promise<void> {
  await fetch(`${FAKE_API_URL}/__fake/revoke`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email }) });
}

/** Every token string the fake API has ever issued (they all start with fake-access- / fake-refresh-). */
export const TOKEN_PATTERN = /fake-(access|refresh)-[0-9a-f-]{36}/;
