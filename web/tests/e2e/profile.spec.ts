import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { FAKE_API_URL, PASSWORD, USER, fakeCalls, signIn } from './support';

/** A fresh account per test (registered with the fake API), so saved profiles and handicaps never collide. */
async function newUser(page: Page): Promise<string> {
  const email = `e2e-${randomUUID()}@example.test`;
  const res = await fetch(`${FAKE_API_URL}/auth/register`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email, password: PASSWORD }),
  });
  expect(res.ok).toBe(true);
  await signIn(page, email);
  return email;
}

test('saving the profile sends every field (PUT /me is a full replace)', async ({ page }) => {
  const email = await newUser(page);
  await page.goto('/profile');
  await expect(page.getByText(`Signed in as ${email}.`)).toBeVisible();
  await page.getByLabel('First name').fill('Robin');
  await page.getByLabel('Last name').fill('Fairway');
  await page.getByLabel('Gender').selectOption('Female');
  await page.getByLabel('GHIN number').fill('1234567');
  await page.getByRole('radio', { name: 'Meters' }).check();
  await page.getByRole('button', { name: 'Save profile' }).click();
  await expect(page).toHaveURL(/\/profile\?saved=profile$/);
  await expect(page.getByRole('status')).toContainText('Profile saved.');
  await expect(page.getByLabel('First name')).toHaveValue('Robin');
  await expect(page.getByLabel('Last name')).toHaveValue('Fairway');

  let put = (await fakeCalls(email)).filter((c) => c.method === 'PUT' && c.path === '/me').at(-1);
  expect(put?.body).toEqual({ firstName: 'Robin', lastName: 'Fairway', gender: 'Female', ghinNumber: '1234567', homeCourseId: null, units: 'Meters' });

  // Changing one field still sends all of them.
  await page.getByLabel('First name').fill('Rob');
  await page.getByRole('button', { name: 'Save profile' }).click();
  await expect(page).toHaveURL(/saved=profile/);
  put = (await fakeCalls(email)).filter((c) => c.method === 'PUT' && c.path === '/me').at(-1);
  expect(Object.keys(put!.body as object).sort()).toEqual(['firstName', 'gender', 'ghinNumber', 'homeCourseId', 'lastName', 'units']);
});

test('a profile the API rejects is shown next to the field', async ({ page, baseURL }) => {
  await newUser(page);
  const res = await page.request.post('/profile', {
    form: { intent: 'profile', firstName: 'x'.repeat(101), lastName: '', gender: 'Male', ghinNumber: '', homeCourseId: '', units: 'Yards' },
    headers: { origin: baseURL! },
    maxRedirects: 0,
  });
  expect(res.status()).toBe(400);
  expect(await res.text()).toContain('Must be at most 100 characters.');
});

test('add and delete handicap index entries', async ({ page }) => {
  const email = await newUser(page);
  await page.goto('/profile');
  const section = page.getByRole('region', { name: 'Handicap index' });
  await expect(section.getByText('No handicap history yet.')).toBeVisible();

  await section.getByLabel('Index').fill('twelve');
  await section.getByRole('button', { name: '+ Add index' }).click();
  await expect(section.locator('#field-index-error')).toContainText('Enter a number');

  await section.getByLabel('Date').fill('2026-09-01');
  await section.getByLabel('Index').fill('12.4');
  await section.getByRole('button', { name: '+ Add index' }).click();
  await expect(page).toHaveURL(/saved=handicap#handicap$/);
  await section.getByLabel('Date').fill('2026-10-01');
  await section.getByLabel('Index').fill('+1.2');
  await section.getByRole('button', { name: '+ Add index' }).click();
  await expect(section.getByRole('status')).toContainText('Handicap index added.');

  // Newest first; a plus handicap reads +1.2 and is stored as −1.2.
  const rows = section.getByRole('table').getByRole('row');
  await expect(rows).toHaveCount(3);
  await expect(rows.nth(1)).toContainText('Oct 1, 2026');
  await expect(rows.nth(1)).toContainText('+1.2');
  await expect(rows.nth(2)).toContainText('12.4');
  await expect(section.locator('.stat-xl')).toHaveText('+1.2');
  const posts = (await fakeCalls(email)).filter((c) => c.method === 'POST' && c.path === '/me/handicap');
  expect(posts.map((p) => p.body)).toEqual([
    { effectiveOn: '2026-09-01', index: 12.4 },
    { effectiveOn: '2026-10-01', index: -1.2 },
  ]);

  // The API's range check lands next to the field.
  await section.getByLabel('Index').fill('60');
  await section.getByRole('button', { name: '+ Add index' }).click();
  await expect(section.locator('#field-index-error')).toHaveText('Must be between -10.0 and 54.0.');

  await section.getByRole('button', { name: /Delete index 12\.4/ }).click();
  await expect(section.getByRole('status')).toContainText('Entry deleted.');
  await expect(section.getByRole('table').getByRole('row')).toHaveCount(2);
  expect((await fakeCalls(email)).some((c) => c.method === 'DELETE' && c.path.startsWith('/me/handicap/'))).toBe(true);
});

test('the profile page shows the existing handicap history', async ({ page }) => {
  await signIn(page, USER);
  await page.goto('/profile');
  await expect(page.getByRole('region', { name: 'Handicap index' }).locator('.stat-xl')).toHaveText('11.8');
});
