import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { ADMIN, FAKE_API_URL, PASSWORD, SESSION_COOKIE, fakeCalls, signIn } from './support';

/** A fresh account per test: deleting one of the shared fixture users would break other tests. */
async function newUser(page: Page): Promise<string> {
  const email = `e2e-delete-${randomUUID()}@example.test`;
  const res = await fetch(`${FAKE_API_URL}/auth/register`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email, password: PASSWORD }),
  });
  expect(res.ok).toBe(true);
  await signIn(page, email);
  return email;
}

const deletes = async (email: string) => (await fakeCalls(email)).filter((c) => c.method === 'DELETE' && c.path === '/me');

test('the profile links to a clearly warned delete page', async ({ page }) => {
  await newUser(page);
  await page.goto('/profile');
  await page.getByRole('region', { name: 'Delete account' }).getByRole('link', { name: 'Delete account' }).click();

  await expect(page).toHaveURL(/\/profile\/delete$/);
  await expect(page.getByRole('heading', { name: 'Delete your account' })).toBeVisible();
  const warning = page.getByRole('region', { name: 'This can’t be undone' });
  await expect(warning).toContainText('deleted permanently');
  await expect(warning).toContainText('Courses you added stay available');
  await expect(warning.getByLabel('Password')).toHaveAttribute('type', 'password');
});

test('a missing or wrong password deletes nothing', async ({ page }) => {
  const email = await newUser(page);
  await page.goto('/profile/delete');

  await page.getByRole('button', { name: 'Delete my account' }).click();
  await expect(page.locator('#field-password-error')).toHaveText('Enter your password to confirm.');
  expect(await deletes(email)).toHaveLength(0);

  await page.getByLabel('Password').fill('not-my-password');
  await page.getByRole('button', { name: 'Delete my account' }).click();
  await expect(page.locator('#field-password-error')).toHaveText('The password is incorrect.');
  await expect(page.getByLabel('Password')).toHaveValue('');

  // Still signed in, account intact.
  await page.goto('/profile');
  await expect(page.getByText(`Signed in as ${email}.`)).toBeVisible();
});

test('deleting with the password removes the account and signs out', async ({ page, context }) => {
  const email = await newUser(page);
  await page.goto('/profile/delete');
  await page.getByLabel('Password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Delete my account' }).click();

  await expect(page.getByRole('heading', { name: 'Your account is deleted' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Sign out' })).toHaveCount(0);
  expect((await context.cookies()).some((c) => c.name === SESSION_COOKIE)).toBe(false);

  const sent = await deletes(email);
  expect(sent).toHaveLength(1);
  expect(sent[0]!.body).toEqual({ password: PASSWORD, externalProvider: null, externalIdToken: null });

  // Signed out: protected pages send you to sign in, and the account can't sign in again.
  await page.goto('/profile');
  await expect(page).toHaveURL(/\/sign-in\?next=%2Fprofile$/);
  const login = await fetch(`${FAKE_API_URL}/auth/login`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email, password: PASSWORD }),
  });
  expect(login.status).toBe(401);
});

test('too many wrong passwords are reported, and nothing is deleted', async ({ page }) => {
  const email = await newUser(page);
  await page.goto('/profile/delete');
  for (let i = 0; i < 4; i++) {
    await page.getByLabel('Password').fill(`wrong-${i}`);
    await page.getByRole('button', { name: 'Delete my account' }).click();
    await expect(page.locator('#field-password-error')).toHaveText('The password is incorrect.');
  }
  await page.getByLabel('Password').fill('wrong-4');
  await page.getByRole('button', { name: 'Delete my account' }).click();

  const alert = page.getByRole('alert');
  await expect(alert).toContainText('Not deleted');
  await expect(alert).toContainText('Too many wrong passwords. Try again later.');
  await expect(page.locator('#field-password-error')).toHaveCount(0);
  await page.goto('/profile');
  await expect(page.getByText(`Signed in as ${email}.`)).toBeVisible();
});

test('the only admin is told to make another admin first', async ({ page }) => {
  // admin@example.test is the fake's only admin, so the fake refuses (409) and nothing is deleted.
  await signIn(page, ADMIN);
  await page.goto('/profile/delete');
  await page.getByLabel('Password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Delete my account' }).click();

  const alert = page.getByRole('alert');
  await expect(alert).toContainText('Not deleted');
  await expect(alert).toContainText('You are the only admin. Make another account an admin before deleting yours.');
  await page.goto('/profile');
  await expect(page.getByText(`Signed in as ${ADMIN}.`)).toBeVisible();
});

test('the delete page needs a session', async ({ page }) => {
  await page.goto('/profile/delete');
  await expect(page).toHaveURL(/\/sign-in\?next=%2Fprofile%2Fdelete$/);
});
