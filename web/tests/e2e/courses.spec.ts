import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import type { Schemas } from '../fake-kit';
import { ADMIN, FAKE_API_URL, PASSWORD, USER, fakeCalls, signIn } from './support';

const CEDAR = '11111111-1111-4111-8111-111111111111';
const HERON = '44444444-4444-4444-8444-444444444444';

/** A fresh account per test, so tests running in parallel never share courses or calls. */
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

/** Creates a manual course straight through the fake API (setup for edit tests). */
async function createCourse(email: string, name: string): Promise<string> {
  const login = await fetch(`${FAKE_API_URL}/auth/login`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email, password: PASSWORD }),
  });
  const { accessToken } = (await login.json()) as { accessToken: string };
  const holes = Array.from({ length: 9 }, (_, i) => ({ number: i + 1, par: 4, yardage: 350, strokeIndex: i + 1 }));
  const res = await fetch(`${FAKE_API_URL}/courses`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', authorization: `Bearer ${accessToken}` },
    body: JSON.stringify({ name, tees: [{ name: 'White', rating: 35.1, slope: 120, holes }] }),
  });
  expect(res.status).toBe(201);
  return ((await res.json()) as Schemas['CourseDetailDto']).id;
}

async function fillHole(page: Page, hole: number, par: string, yards: string, si: string, tee = 0): Promise<void> {
  const scope = page.locator(`#tee-${tee}, #new-tee`).filter({ has: page.getByRole('textbox', { name: `Hole ${hole} par` }) }).first();
  await scope.getByRole('textbox', { name: `Hole ${hole} par` }).fill(par);
  await scope.getByRole('textbox', { name: `Hole ${hole} yards` }).fill(yards);
  await scope.getByRole('textbox', { name: `Hole ${hole} stroke index` }).fill(si);
}

test('search finds a course in our database and shows its tees as scorecards', async ({ page }) => {
  await signIn(page, USER);
  await page.goto('/courses');
  await page.getByLabel('Course, club or town').fill('cedar');
  await page.getByRole('button', { name: 'Search' }).click();
  await expect(page).toHaveURL(/\/courses\?q=cedar/);
  await page.getByRole('link', { name: /Cedar Ridge/ }).click();

  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Cedar Ridge');
  const blue = page.getByRole('article', { name: 'Blue' });
  // The version in use today (Apr 2026 re-rating), with the older one in the history.
  await expect(blue.locator('dd.stat').nth(1)).toHaveText('72.1');
  await expect(blue.getByRole('table', { name: /Versions of the Blue tee/ }).getByRole('row')).toHaveCount(3);
  await expect(blue.getByText('In use today')).toBeVisible();
  await expect(blue.getByRole('table', { name: 'Blue tee: front nine' })).toContainText('Out');
  await expect(blue.getByRole('table', { name: 'Blue tee: back nine' })).toContainText('Total');
});

test('a course not in our database: search the course database, import it, then it shows as already imported', async ({ page }) => {
  const email = await newUser(page);
  const name = `lakeside ${randomUUID().slice(0, 8)}`;
  await page.goto(`/courses?q=${encodeURIComponent(name)}`);
  await expect(page.getByRole('heading', { name: new RegExp(`No courses match`) })).toBeVisible();
  await page.getByRole('link', { name: 'Search the course database' }).click();
  await expect(page).toHaveURL(new RegExp(`/courses/provider-search\\?q=`));
  await expect(page.getByLabel('Course or club name')).toHaveValue(name);

  await page.getByRole('button', { name: /^Import .*Championship/i }).click();
  await expect(page).toHaveURL(/\/courses\/[0-9a-f-]{36}\?imported=1$/);
  await expect(page.getByRole('status')).toContainText('Course imported');
  const courseUrl = new URL(page.url()).pathname;
  const imports = (await fakeCalls(email)).filter((c) => c.path === '/courses/import');
  expect(imports).toHaveLength(1);
  expect(imports[0]!.body).toEqual({ providerCourseId: `gca-q-${name.replace(/ /g, '-')}` });

  // Searching again links to our copy instead of importing it twice.
  await page.goto(`/courses/provider-search?q=${encodeURIComponent(name)}`);
  const row = page.getByRole('listitem').filter({ hasText: 'Already imported' });
  await expect(row.getByRole('link', { name: /View course/ })).toHaveAttribute('href', courseUrl);
  await expect(page.getByRole('button', { name: /^Import/ })).toHaveCount(0);
});

test('the course database search shows a course already imported by someone else as a link', async ({ page }) => {
  await signIn(page, USER);
  await page.goto('/courses/provider-search?q=cedar');
  await expect(page.getByRole('link', { name: /View course.*Cedar Ridge/ })).toHaveAttribute('href', `/courses/${CEDAR}`);
});

test('a course database outage is shown as a message', async ({ page }) => {
  await signIn(page, USER);
  const res = await page.goto('/courses/provider-search?q=unavailable');
  expect(res?.status()).toBe(502);
  await expect(page.getByRole('alert')).toContainText('The course provider could not be reached');
});

test('a course database course with no usable tees is not imported (502)', async ({ page }) => {
  const email = await newUser(page);
  const name = `notees ${randomUUID().slice(0, 8)}`;
  await page.goto(`/courses/provider-search?q=${encodeURIComponent(name)}`);
  const response = page.waitForResponse((r) => r.request().method() === 'POST' && r.url().includes('/courses/provider-search'));
  await page.getByRole('button', { name: /^Import .*Championship/i }).click();
  expect((await response).status()).toBe(502);
  const alert = page.getByRole('alert');
  await expect(alert).toContainText('The course wasn’t imported');
  await expect(alert).toContainText('The course provider returned no tee with 1 to 18 holes for this course, so nothing was imported.');
  // Nothing was imported: the result still offers Import, and our database has no such course.
  await expect(page.getByRole('button', { name: /^Import .*Championship/i })).toBeVisible();
  await expect(page.getByText('Already imported')).toHaveCount(0);
  await page.goto(`/courses?q=${encodeURIComponent(name)}`);
  await expect(page.getByRole('heading', { name: /No courses match/ })).toBeVisible();
  expect((await fakeCalls(email)).filter((c) => c.path === '/courses/import')).toHaveLength(1);
});

test('manual entry: errors are shown next to their fields, including holes the API rejects', async ({ page }) => {
  const email = await newUser(page);
  await page.goto('/courses/new');
  await page.getByRole('button', { name: 'Save course' }).click();
  await expect(page.getByRole('alert')).toHaveText('Check the highlighted fields.');
  await expect(page.locator('#field-name-error')).toHaveText('Name is required.');
  await expect(page.locator('#f-tees-0--name-error')).toHaveText('A tee name is required.');
  await expect(page.locator('#f-tees-0--rating-error')).toHaveText('Rating is required.');
  // Nothing was sent: the form caught these first.
  expect((await fakeCalls(email)).some((c) => c.path === '/courses' && c.method === 'POST')).toBe(false);

  const name = `Mill Pond ${randomUUID().slice(0, 8)}`;
  await page.getByLabel('Course name').fill(name);
  await page.getByLabel('Tee name').fill('Gold');
  await page.getByLabel('Course rating').fill('70.2');
  await page.getByLabel('Slope', { exact: true }).fill('124');
  // Hole 2 left blank: hole 3 is the second hole sent, and the API's error for it must land on row 3.
  await fillHole(page, 1, '4', '380', '1');
  await fillHole(page, 3, '9', '170', '2');
  await page.getByRole('button', { name: 'Save course' }).click();
  await expect(page.getByRole('textbox', { name: 'Hole 3 par' })).toHaveAttribute('aria-invalid', 'true');
  await expect(page.locator('#f-tees-0--holes-2-par-error')).toHaveText('Par must be between 3 and 6.');
  await expect(page.getByLabel('Course name')).toHaveValue(name);

  await page.getByRole('textbox', { name: 'Hole 3 par' }).fill('3');
  const formId = await page.locator('input[name="id"]').inputValue();
  await page.getByRole('button', { name: 'Save course' }).click();
  await expect(page).toHaveURL(new RegExp(`/courses/${formId}\\?saved=created$`));
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(name);
  await expect(page.getByRole('article', { name: 'Gold' }).getByRole('table', { name: 'Gold tee: holes' })).toContainText('Total');

  const posts = (await fakeCalls(email)).filter((c) => c.path === '/courses' && c.method === 'POST');
  const body = posts.at(-1)!.body as Schemas['CreateCourseRequest'];
  expect(body.id).toBe(formId);
  expect(body.tees![0]!.id).toMatch(/^[0-9a-f-]{36}$/);
  expect(body.tees![0]!.holes).toEqual([
    { number: 1, par: 4, yardage: 380, strokeIndex: 1 },
    { number: 3, par: 3, yardage: 170, strokeIndex: 2 },
  ]);
});

test('manual entry: posting the same form again returns the course already created, not a duplicate', async ({ page, baseURL }) => {
  const email = await newUser(page);
  await page.goto('/courses/new');
  const name = `Retry Links ${randomUUID().slice(0, 8)}`;
  await page.getByLabel('Course name').fill(name);
  await page.getByLabel('Tee name').fill('Blue');
  await page.getByLabel('Course rating').fill('35.0');
  await page.getByLabel('Slope', { exact: true }).fill('118');
  await fillHole(page, 1, '4', '360', '1');
  const form = Object.fromEntries(
    await page.locator('form.course-form').evaluate((f) => [...new FormData(f as HTMLFormElement).entries()].map(([k, v]) => [k, String(v)])),
  ) as Record<string, string>;
  await page.getByRole('button', { name: 'Save course' }).click();
  await expect(page).toHaveURL(/\?saved=created$/);
  const first = new URL(page.url()).pathname;

  const retry = await page.request.post('/courses/new', { form: { ...form, intent: 'save' }, headers: { origin: baseURL! }, maxRedirects: 0 });
  expect(retry.status()).toBe(303);
  expect(retry.headers()['location']).toBe(`${first}?saved=created`);
  const posts = (await fakeCalls(email)).filter((c) => c.path === '/courses' && c.method === 'POST');
  expect(posts).toHaveLength(2);
  expect((posts[0]!.body as { id: string }).id).toBe((posts[1]!.body as { id: string }).id);
});

test('manual entry: after a 409 (same form, different name) the form gets new ids so saving again works', async ({ page, baseURL }) => {
  await newUser(page);
  await page.goto('/courses/new');
  const name = `Conflict Hills ${randomUUID().slice(0, 8)}`;
  await page.getByLabel('Course name').fill(name);
  await page.getByLabel('Tee name').fill('Blue');
  await page.getByLabel('Course rating').fill('35.0');
  await page.getByLabel('Slope', { exact: true }).fill('118');
  await fillHole(page, 1, '4', '360', '1');
  const form = Object.fromEntries(
    await page.locator('form.course-form').evaluate((f) => [...new FormData(f as HTMLFormElement).entries()].map(([k, v]) => [k, String(v)])),
  ) as Record<string, string>;
  await page.getByRole('button', { name: 'Save course' }).click();
  await expect(page).toHaveURL(/\?saved=created$/);

  const post = (data: Record<string, string>) =>
    page.request.post('/courses/new', { form: { ...data, intent: 'save' }, headers: { origin: baseURL! }, maxRedirects: 0 });
  const conflict = await post({ ...form, name: `${name} II` });
  expect(conflict.status()).toBe(409);
  const html = await conflict.text();
  expect(html).toContain('A course with this id already exists. Save again to add it as a new course.');
  const hidden = (field: string) => new RegExp(`name="${field.replace(/[[\].]/g, '\\$&')}" value="([^"]+)"`).exec(html)?.[1];
  const newId = hidden('id');
  const newTeeId = hidden('tees[0].id');
  expect(newId).toMatch(/^[0-9a-f-]{36}$/);
  expect(newId).not.toBe(form.id);
  expect(newTeeId).toMatch(/^[0-9a-f-]{36}$/);
  expect(newTeeId).not.toBe(form['tees[0].id']);

  const again = await post({ ...form, name: `${name} II`, id: newId!, 'tees[0].id': newTeeId! });
  expect(again.status()).toBe(303);
  expect(again.headers()['location']).toBe(`/courses/${newId}?saved=created`);
});

test('manual entry: "Add another tee" adds a tee with par and stroke index copied, without a script', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await newUser(page);
  await page.goto('/courses/new');
  await page.getByLabel('Tee name').fill('Blue');
  await fillHole(page, 1, '5', '510', '3');
  await page.getByRole('button', { name: 'Add another tee' }).click();
  await expect(page.getByRole('heading', { name: 'Blue tee' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Tee 2' })).toBeVisible();
  const second = page.locator('#new-tee');
  await expect(second.getByRole('textbox', { name: 'Hole 1 par' })).toHaveValue('5');
  await expect(second.getByRole('textbox', { name: 'Hole 1 stroke index' })).toHaveValue('3');
  await expect(second.getByRole('textbox', { name: 'Hole 1 yards' })).toHaveValue('');
  // Tee ids differ and survive the round trip.
  const ids = await page.locator('input[name$="].id"]').evaluateAll((els) => els.map((e) => (e as HTMLInputElement).value));
  expect(new Set(ids).size).toBe(2);

  await page.getByRole('button', { name: 'Remove tee 2' }).click();
  await expect(page.getByRole('heading', { name: 'Tee 2' })).toHaveCount(0);
  await expect(page.getByLabel('Tee name')).toHaveValue('Blue');
  await context.close();
});

test('editing a Verified course as a golfer sends it for review (202)', async ({ page }) => {
  const email = await newUser(page);
  await page.goto(`/courses/${HERON}/edit`);
  await expect(page.getByText('This course is verified.')).toBeVisible();
  await page.getByLabel('Course name').fill('Heron Bay Links (renamed)');
  await page.getByRole('button', { name: 'Send for review' }).click();
  await expect(page).toHaveURL(new RegExp(`/courses/${HERON}\\?review=1$`));
  await expect(page.getByRole('status')).toContainText('Sent for review');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Heron Bay Links');
  const put = (await fakeCalls(email)).find((c) => c.method === 'PUT' && c.path === `/courses/${HERON}`);
  expect(put?.body).toMatchObject({ name: 'Heron Bay Links (renamed)', city: 'Port Clement' });
});

test('editing an unverified course saves it', async ({ page }) => {
  const email = await newUser(page);
  const id = await createCourse(email, `Elm Park ${randomUUID().slice(0, 8)}`);
  await page.goto(`/courses/${id}/edit`);
  await page.getByLabel('City').fill('Springfield');
  await page.getByLabel('Course name').fill('');
  await page.getByRole('button', { name: 'Save details' }).click();
  await expect(page.locator('#field-name-error')).toHaveText('Name is required.');
  await page.getByLabel('Course name').fill('Elm Park Renamed');
  await page.getByRole('button', { name: 'Save details' }).click();
  await expect(page).toHaveURL(new RegExp(`/courses/${id}\\?saved=details$`));
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Elm Park Renamed');
  await expect(page.getByText('Springfield')).toBeVisible();
});

test('add a tee and a new tee version', async ({ page }) => {
  const email = await newUser(page);
  const id = await createCourse(email, `Birch Hollow ${randomUUID().slice(0, 8)}`);

  await page.goto(`/courses/${id}/tees/new`);
  // Par and stroke index start from the first tee.
  await expect(page.getByRole('textbox', { name: 'Hole 1 par' })).toHaveValue('4');
  await page.getByLabel('Tee name').fill('Red');
  await page.getByLabel('Rated for').selectOption('Female');
  await page.getByLabel('Course rating').fill('33.9');
  await page.getByLabel('Slope', { exact: true }).fill('112');
  for (let h = 1; h <= 9; h++) await page.getByRole('textbox', { name: `Hole ${h} yards` }).fill('300');
  await page.getByRole('button', { name: 'Add tee' }).click();
  await expect(page).toHaveURL(/\?saved=tee$/);
  await expect(page.getByRole('article', { name: 'Red' })).toContainText('Rated for women');

  await page.getByRole('link', { name: 'New version of the White tee' }).click();
  await expect(page.getByLabel('Course rating')).toHaveValue('35.1');
  await page.getByLabel('Course rating').fill('35.6');
  await page.getByRole('button', { name: 'Save new version' }).click();
  await expect(page.locator('#f-effectiveFrom-error')).toHaveText('The date the change took effect is required.');
  await page.getByLabel('Effective from').fill('2026-03-15');
  await page.getByRole('button', { name: 'Save new version' }).click();
  await expect(page).toHaveURL(/\?saved=version$/);
  const white = page.getByRole('article', { name: 'White' });
  await expect(white.getByRole('table', { name: /Versions of the White tee/ }).getByRole('row')).toHaveCount(3);
  await expect(white.locator('dd.stat').nth(1)).toHaveText('35.6');
  const version = (await fakeCalls(email)).find((c) => c.method === 'POST' && c.path.endsWith('/versions'));
  expect(version?.body).toMatchObject({ effectiveFrom: '2026-03-15', rating: 35.6, slope: 120 });
});

test('flag a problem with a course', async ({ page }) => {
  const email = await newUser(page);
  await page.goto(`/courses/${CEDAR}`);
  const report = page.getByRole('region', { name: 'Something wrong?' });
  await report.getByRole('button', { name: 'Send report' }).click();
  await expect(report.locator('#flag-note-error')).toHaveText('A note is required.');
  await report.getByLabel('Tee').selectOption({ label: 'Red' });
  await report.getByLabel('Hole').fill('7');
  await report.getByLabel('What’s wrong').fill('Hole 7 is 140 yards from the red tee now.');
  await report.getByRole('button', { name: 'Send report' }).click();
  await expect(page).toHaveURL(/\?flagged=1#report$/);
  await expect(report.getByRole('status')).toContainText('An admin will check it');
  const flag = (await fakeCalls(email)).filter((c) => c.path === `/courses/${CEDAR}/flags`).at(-1);
  expect(flag?.body).toEqual({ teeId: '22222222-2222-4222-8222-222222222223', holeNumber: 7, note: 'Hole 7 is 140 yards from the red tee now.' });
});

test('a flag the API rejects for its tee shows the error under the tee select', async ({ page }) => {
  await newUser(page);
  await page.goto(`/courses/${CEDAR}`);
  const report = page.getByRole('region', { name: 'Something wrong?' });
  // A tee from another course (as if the page were stale): the API answers 400 keyed "teeId".
  await report.locator('#flag-tee option').nth(1).evaluate((o) => ((o as HTMLOptionElement).value = '55555555-5555-4555-8555-555555555555'));
  await report.getByLabel('Tee').selectOption({ index: 1 });
  await report.getByLabel('What’s wrong').fill('Wrong yardage.');
  await report.getByRole('button', { name: 'Send report' }).click();
  const select = report.getByLabel('Tee');
  await expect(select).toHaveAttribute('aria-invalid', 'true');
  await expect(select).toHaveAttribute('aria-describedby', 'flag-tee-error');
  await expect(report.locator('#flag-tee-error')).toHaveText("The tee doesn't belong to this course.");
});

test('make a course my home course sends the whole profile', async ({ page }) => {
  const email = await newUser(page);
  await page.goto(`/courses/${CEDAR}`);
  await page.getByRole('button', { name: 'Make this my home course' }).click();
  await expect(page).toHaveURL(/\?saved=home$/);
  const put = (await fakeCalls(email)).find((c) => c.method === 'PUT' && c.path === '/me');
  expect(put?.body).toEqual({ firstName: '', lastName: '', gender: 'Male', ghinNumber: null, homeCourseId: CEDAR, units: 'Yards' });
});

test('an admin edits a Verified course page without the review note', async ({ page }) => {
  await signIn(page, ADMIN);
  await page.goto(`/courses/${HERON}/edit`);
  await expect(page.getByRole('button', { name: 'Save details' })).toBeVisible();
  await expect(page.getByText('This course is verified.')).toHaveCount(0);
});

test('an unknown course is a 404 page', async ({ page }) => {
  await signIn(page, USER);
  const res = await page.goto('/courses/99999999-9999-4999-8999-999999999999');
  expect(res?.status()).toBe(404);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Course not found');
});
