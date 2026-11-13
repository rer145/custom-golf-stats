import { expect, test } from '@playwright/test';
import { ADMIN, DISABLED, PASSWORD, USER, signIn } from './support';

test('landing page shows the tagline and the way in', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Track the stats nobody else thought to.');
  await expect(page.getByRole('link', { name: 'Create an account' })).toHaveAttribute('href', '/sign-up');
  await expect(page.getByRole('main').getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/sign-in');
  await expect(page.getByRole('img', { name: 'Custom Golf Stats' })).toHaveAttribute('src', '/logo/lockup-horizontal-on-dark.svg');
});

test('signing in lands on stats with the main navigation', async ({ page }) => {
  await signIn(page, USER);
  await expect(page).toHaveURL('/stats');
  const nav = page.getByRole('navigation', { name: 'Main' });
  await expect(nav.getByRole('link')).toHaveText(['Stats', 'Rounds', 'Courses', 'Profile']);
  await expect(nav.getByRole('link', { name: 'Stats' })).toHaveAttribute('aria-current', 'page');
});

test('admins also get the Admin link', async ({ page }) => {
  await signIn(page, ADMIN);
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link')).toHaveText(['Stats', 'Rounds', 'Courses', 'Profile', 'Admin']);
  const res = await page.goto('/admin');
  expect(res?.status()).toBe(200);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Admin');
});

test('a non-admin gets the 403 page on /admin', async ({ page }) => {
  await signIn(page, USER);
  const res = await page.goto('/admin');
  expect(res?.status()).toBe(403);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('No access');
});

test('wrong password shows one message and keeps the email', async ({ page }) => {
  await page.goto('/sign-in');
  await page.getByLabel('Email').fill(USER);
  await page.getByLabel('Password').fill('not-the-password');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('alert')).toHaveText('That email and password don’t match an account.');
  await expect(page.getByLabel('Email')).toHaveValue(USER);
  await expect(page.getByLabel('Password')).toHaveValue('');
  await expect(page).toHaveURL('/sign-in');
});

test('a disabled account is told it cannot sign in', async ({ page }) => {
  await page.goto('/sign-in');
  await page.getByLabel('Email').fill(DISABLED);
  await page.getByLabel('Password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('alert')).toContainText('can’t sign in');
});

test('empty sign-in shows errors next to the fields', async ({ page }) => {
  await page.goto('/sign-in');
  await page.getByRole('button', { name: 'Sign in' }).click();
  const email = page.getByLabel('Email');
  await expect(email).toHaveAttribute('aria-invalid', 'true');
  await expect(page.locator(`#${await email.getAttribute('aria-describedby')}`)).toHaveText('Enter your email address.');
  const password = page.getByLabel('Password');
  await expect(page.locator(`#${await password.getAttribute('aria-describedby')}`)).toHaveText('Enter your password.');
});

test('signing in returns to the page that was asked for', async ({ page }) => {
  await page.goto('/rounds?year=2024');
  await expect(page).toHaveURL('/sign-in?next=%2Frounds%3Fyear%3D2024');
  await page.getByLabel('Email').fill(USER);
  await page.getByLabel('Password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL('/rounds?year=2024');
});

test('next cannot send the user off-site', async ({ page }) => {
  await signIn(page, USER, PASSWORD, 'https://evil.example/');
  await expect(page).toHaveURL('/stats');
});

test('sign-up shows API validation next to the fields', async ({ page }) => {
  await page.goto('/sign-up');
  await page.getByLabel('Email').fill(USER);
  await page.getByLabel('Password').fill('abc');
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page.getByRole('alert')).toHaveText('Check the highlighted fields.');
  await expect(page.getByLabel('Email')).toHaveAttribute('aria-invalid', 'true');
  await expect(page.locator('.field-error').first()).toContainText('is already taken');
  await expect(page.locator('.field-error').nth(1)).toContainText('at least 6 characters');
  await expect(page.locator('.field-error').nth(1)).toContainText('at least one digit');
});

test('sign-up registers, signs in and goes to the profile', async ({ page, context }) => {
  const email = `new-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.test`;
  await page.goto('/sign-up');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('Brand-new-1');
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page).toHaveURL('/profile');
  await expect(page.getByText(`Signed in as ${email}.`)).toBeVisible();
  expect((await context.cookies()).some((c) => c.name === 'cgs_session')).toBe(true);
});

test('sign-out clears the session', async ({ page, context }) => {
  await signIn(page, USER);
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL('/');
  expect((await context.cookies()).some((c) => c.name === 'cgs_session')).toBe(false);
  await page.goto('/stats');
  await expect(page).toHaveURL('/sign-in?next=%2Fstats');
});

test('a signed-in user opening sign-in goes straight to stats', async ({ page }) => {
  await signIn(page, USER);
  await page.goto('/sign-in');
  await expect(page).toHaveURL('/stats');
});

test('unknown pages are guarded, then 404 once signed in', async ({ page }) => {
  await page.goto('/no-such-page');
  await expect(page).toHaveURL('/sign-in?next=%2Fno-such-page');
  await signIn(page, USER);
  const res = await page.goto('/no-such-page');
  expect(res?.status()).toBe(404);
});
