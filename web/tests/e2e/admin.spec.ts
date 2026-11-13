import { expect, test, type Page } from '@playwright/test';
import { listPagePaths } from '../support/pages';
import { ADMIN, USER, fakeCalls, signIn } from './support';

const adminPaths = listPagePaths().filter((p) => p.path === '/admin' || p.path.startsWith('/admin/'));

const course = (n: number) => `ad000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const tee = (n: number) => `ad222222-0000-4000-8000-${String(n).padStart(12, '0')}`;
const version = (n: number) => `ad444444-0000-4000-8000-${String(n).padStart(12, '0')}`;
const versionUrl = (n: number) => `/admin/courses/${course(n)}/tees/${tee(n)}/versions/${version(n)}`;

/** The admin's API calls matching a method and path (a string or pattern), in order. */
async function adminCalls(method: string, path: string | RegExp) {
  return (await fakeCalls(ADMIN)).filter((c) => c.method === method && (typeof path === 'string' ? c.path === path : path.test(c.path)));
}

test.describe('every admin route is closed to a non-admin', () => {
  test('the admin pages are enumerated from disk', () => {
    expect(adminPaths.map((p) => p.file)).toEqual(
      expect.arrayContaining([
        'admin/index.astro',
        'admin/users.astro',
        'admin/courses/index.astro',
        'admin/courses/[id]/index.astro',
        'admin/courses/[id]/merge.astro',
        'admin/courses/[id]/tees/[teeId]/versions/[versionId].astro',
        'admin/flags.astro',
        'admin/settings.astro',
      ]),
    );
  });

  for (const { file, path } of adminPaths) {
    test(`${file} (${path}): GET and POST are 403 and reach no admin API`, async ({ page }) => {
      await signIn(page, USER);
      const get = await page.request.get(path, { maxRedirects: 0 });
      expect(get.status(), `GET ${path}`).toBe(403);
      expect(await get.text()).toContain('No access');
      const post = await page.request.post(path, { form: { id: 'x', decision: 'resolve', action: 'verify' }, maxRedirects: 0, headers: { origin: 'http://127.0.0.1:4321' } });
      expect(post.status(), `POST ${path}`).toBe(403);
      const adminApi = (await fakeCalls(USER)).filter((c) => c.path.startsWith('/admin'));
      expect(adminApi).toEqual([]);
    });
  }
});

test.describe('overview', () => {
  test('shows the open flag count and courses by status as numbers', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto('/admin');
    await expect(page.getByRole('heading', { name: 'Admin', level: 1 })).toBeVisible();
    for (const id of ['flags', 'imported', 'edited', 'verified']) {
      await expect(page.locator(`[data-tile="${id}"] [data-value]`)).toHaveText(/^\s*\d+\+?\s*$/);
    }
    // Seeded: at least three open flags and five imported courses (other tests may close or verify some).
    expect(Number(await page.locator('[data-tile="imported"] [data-value]').innerText())).toBeGreaterThanOrEqual(1);
    await page.getByRole('link', { name: 'Open flags' }).click();
    await expect(page).toHaveURL('/admin/flags');
  });
});

async function userRow(page: Page, email: string) {
  await page.goto(`/admin/users?query=${encodeURIComponent(email)}`);
  const row = page.locator(`tr[data-user="${email}"]`);
  await expect(row).toBeVisible();
  return row;
}

test.describe('users', () => {
  test('search narrows the list', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto('/admin/users');
    await page.getByLabel('Search by email or name').fill('searchable');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(page.locator('tr[data-user]')).toHaveCount(1);
    await expect(page.locator('tr[data-user="delta.search@golfers.test"]')).toBeVisible();
    await page.getByLabel('Search by email or name').fill('no-such-golfer');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(page.getByText('No golfers match that search.')).toBeVisible();
  });

  test('sets the tier', async ({ page }) => {
    await signIn(page, ADMIN);
    const email = 'alpha.tier@golfers.test';
    const row = await userRow(page, email);
    await row.getByLabel(`Tier for ${email}`).selectOption('Paid');
    await row.getByRole('button', { name: `Save ${email}` }).click();
    await expect(page.getByRole('status')).toContainText(`Saved changes for ${email}`);
    await expect(page.locator(`tr[data-user="${email}"]`).getByLabel(`Tier for ${email}`)).toHaveValue('Paid');
    const calls = (await adminCalls('PUT', /^\/admin\/users\//)).filter((c) => c.path.endsWith('ad111111-0000-4000-8000-000000000001'));
    expect(calls.at(-1)?.body).toEqual({ tier: 'Paid', role: 'User', disabled: false });
  });

  test('sets the role', async ({ page }) => {
    await signIn(page, ADMIN);
    const email = 'bravo.role@golfers.test';
    const row = await userRow(page, email);
    await row.getByLabel(`Role for ${email}`).selectOption('Admin');
    await row.getByRole('button', { name: `Save ${email}` }).click();
    await expect(page.getByRole('status')).toContainText(`Saved changes for ${email}`);
    await expect(page.locator(`tr[data-user="${email}"]`).getByLabel(`Role for ${email}`)).toHaveValue('Admin');
    const calls = (await adminCalls('PUT', /^\/admin\/users\//)).filter((c) => c.path.endsWith('ad111111-0000-4000-8000-000000000002'));
    expect(calls.at(-1)?.body).toMatchObject({ role: 'Admin' });
  });

  test('disables and re-enables an account', async ({ page }) => {
    await signIn(page, ADMIN);
    const email = 'charlie.disable@golfers.test';
    let row = await userRow(page, email);
    await row.getByRole('checkbox', { name: `Disabled: ${email}` }).check();
    await row.getByRole('button', { name: `Save ${email}` }).click();
    await expect(page.getByRole('status')).toContainText(`Saved changes for ${email}`);
    row = page.locator(`tr[data-user="${email}"]`);
    await expect(row.getByRole('checkbox', { name: `Disabled: ${email}` })).toBeChecked();
    await row.getByRole('checkbox', { name: `Disabled: ${email}` }).uncheck();
    await row.getByRole('button', { name: `Save ${email}` }).click();
    await expect(page.locator(`tr[data-user="${email}"]`).getByRole('checkbox', { name: `Disabled: ${email}` })).not.toBeChecked();
    const calls = (await adminCalls('PUT', /^\/admin\/users\//)).filter((c) => c.path.endsWith('ad111111-0000-4000-8000-000000000003'));
    expect(calls.map((c) => (c.body as { disabled: boolean }).disabled)).toEqual([true, false]);
  });

  test("an admin can't demote or disable themselves: the API's message is shown", async ({ page }) => {
    await signIn(page, ADMIN);
    const row = await userRow(page, ADMIN);
    await expect(row).toContainText('(you)');
    await row.getByLabel(`Role for ${ADMIN}`).selectOption('User');
    await row.getByRole('checkbox', { name: `Disabled: ${ADMIN}` }).check();
    const [response] = await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST'), row.getByRole('button', { name: `Save ${ADMIN}` }).click()]);
    expect(response.status()).toBe(400);
    await expect(page.getByRole('alert')).toContainText('Not saved');
    await expect(page.locator('[data-row-error]')).toContainText("You can't remove your own Admin role.");
    await expect(page.locator('[data-row-error]')).toContainText("You can't disable your own account.");
    // Still an admin.
    await page.goto(`/admin/users?query=${encodeURIComponent(ADMIN)}`);
    await expect(page.locator(`tr[data-user="${ADMIN}"]`).getByLabel(`Role for ${ADMIN}`)).toHaveValue('Admin');
  });
});

test.describe('courses', () => {
  test('filters by status and by search', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto('/admin/courses?status=Verified');
    await expect(page.locator('tr[data-course="Dunmore Park"]')).toBeVisible();
    await expect(page.locator('tr[data-course]').filter({ hasText: 'Imported' })).toHaveCount(0);
    await page.goto('/admin/courses');
    await page.getByLabel('Search by name, club or city').fill('Dunmore');
    await page.getByLabel('Status', { exact: true }).selectOption('Verified');
    await page.getByRole('button', { name: 'Filter' }).click();
    await expect(page).toHaveURL(/status=Verified/);
    await expect(page.locator('tr[data-course]')).toHaveCount(1);
    await page.getByRole('link', { name: 'Edit Dunmore Park' }).click();
    await expect(page.getByRole('heading', { name: 'Dunmore Park', level: 1 })).toBeVisible();
  });

  test('edits details', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto(`/admin/courses/${course(3)}`);
    await expect(page.getByRole('heading', { name: 'Cedar Hollow', level: 1 })).toBeVisible();
    await page.getByLabel('City').fill('Springfield');
    await page.getByLabel('Latitude').fill('44.05');
    await page.getByRole('button', { name: 'Save details' }).click();
    await expect(page.getByRole('status')).toContainText('Course details saved.');
    await expect(page.getByLabel('City')).toHaveValue('Springfield');
    await expect(page.locator('[data-status]').first()).toHaveText('User edited');
    const put = (await adminCalls('PUT', `/admin/courses/${course(3)}`)).at(-1);
    expect(put?.body).toMatchObject({ name: 'Cedar Hollow', city: 'Springfield', latitude: 44.05, longitude: null, phone: null });
  });

  test('shows validation errors next to the fields and keeps what was typed', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto(`/admin/courses/${course(11)}`);
    await page.getByLabel('Name', { exact: true }).fill('');
    await page.getByLabel('City').fill('Elsewhere');
    await page.getByLabel('Latitude').fill('north');
    const [response] = await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST'), page.getByRole('button', { name: 'Save details' }).click()]);
    expect(response.status()).toBe(400);
    await expect(page.getByLabel('Latitude')).toHaveAttribute('aria-invalid', 'true');
    await expect(page.getByText('Enter a number.')).toBeVisible();
    await expect(page.getByLabel('City')).toHaveValue('Elsewhere');
    await page.getByLabel('Latitude').fill('');
    await page.getByRole('button', { name: 'Save details' }).click();
    await expect(page.getByLabel('Name', { exact: true })).toHaveAttribute('aria-invalid', 'true');
    await expect(page.getByText('Name is required.')).toBeVisible();
  });

  test('verifies a course', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto(`/admin/courses/${course(2)}`);
    await page.getByRole('button', { name: 'Verify course' }).click();
    await expect(page.getByRole('status')).toContainText('Course verified.');
    await expect(page.locator('#status-h + p [data-status]')).toHaveText('Verified');
    expect((await adminCalls('POST', `/admin/courses/${course(2)}/verify`)).length).toBe(1);
  });

  test('an unknown course shows a not-found message, not an error page', async ({ page }) => {
    await signIn(page, ADMIN);
    const res = await page.goto('/admin/courses/99999999-9999-4999-8999-999999999999');
    expect(res?.status()).toBe(404);
    await expect(page.getByRole('alert')).toBeVisible();
  });
});

test.describe('correcting a tee version', () => {
  test('warns that played rounds change, then corrects the version in place', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto(`/admin/courses/${course(9)}`);
    await page.getByRole('link', { name: /Correct Blue version from 2020-01-01/ }).click();
    await expect(page.getByRole('heading', { name: 'Correct tee version', level: 1 })).toBeVisible();
    await expect(page.locator('[data-warning]')).toContainText('This changes played rounds.');
    await page.getByLabel('Rating', { exact: true }).fill('72.1');
    await page.getByLabel('Hole 4 par').fill('4');
    await page.getByLabel('I understand this changes rounds already played on this version.').check();
    await page.getByRole('button', { name: 'Correct version' }).click();
    await expect(page).toHaveURL(new RegExp(`/admin/courses/${course(9)}\\?saved=version`));
    await expect(page.getByRole('status')).toContainText('Tee version corrected.');
    const put = (await adminCalls('PUT', versionUrl(9))).at(-1);
    const body = put?.body as { rating: number; slope: number; holes: { number: number; par: number }[] };
    expect(body.rating).toBe(72.1);
    expect(body.slope).toBe(128);
    expect(body.holes).toHaveLength(18);
    expect(body.holes[3]).toMatchObject({ number: 4, par: 4 });
  });

  test('needs the confirmation and sends nothing without it', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto(versionUrl(1));
    await page.getByLabel('Rating', { exact: true }).fill('70.0');
    await page.getByRole('button', { name: 'Correct version' }).click();
    await expect(page.getByText('Confirm that you understand this changes rounds already played.')).toBeVisible();
    await expect(page.getByLabel('Rating', { exact: true })).toHaveValue('70.0');
    expect(await adminCalls('PUT', versionUrl(1))).toEqual([]);
  });

  test("shows the API's validation errors against the right fields and hole", async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto(versionUrl(11));
    await page.getByLabel('Slope', { exact: true }).fill('10');
    await page.getByLabel('Hole 3 par').fill('9');
    await page.getByLabel('I understand this changes rounds already played on this version.').check();
    await page.getByRole('button', { name: 'Correct version' }).click();
    await expect(page.getByLabel('Slope', { exact: true })).toHaveAttribute('aria-invalid', 'true');
    await expect(page.getByText('Slope must be between 55 and 155.')).toBeVisible();
    await expect(page.locator('tr[data-hole="3"] [data-row-error]')).toContainText('Par must be between 3 and 6.');
    await expect(page.getByLabel('Hole 3 par')).toHaveValue('9');
  });

  test('rejects removing played holes with a 409 that names them', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto(versionUrl(10));
    await page.getByRole('checkbox', { name: 'Keep hole 4' }).uncheck();
    await page.getByRole('checkbox', { name: 'Keep hole 5' }).uncheck();
    await page.getByRole('checkbox', { name: 'Keep hole 6' }).uncheck();
    await page.getByLabel('I understand this changes rounds already played on this version.').check();
    const [response] = await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST'), page.getByRole('button', { name: 'Correct version' }).click()]);
    expect(response.status()).toBe(409);
    await expect(page.getByRole('alert').first()).toContainText('have played holes the change removes');
    await expect(page.locator('[data-played-holes]')).toContainText('4, 5');
    // What the admin unticked stays unticked so they can fix it.
    await expect(page.getByRole('checkbox', { name: 'Keep hole 6' })).not.toBeChecked();
  });
});

test.describe('merging', () => {
  async function search(page: Page, from: number, text: string) {
    await page.goto(`/admin/courses/${course(from)}/merge`);
    await page.getByLabel('Search by name, club or city').fill(text);
    await page.getByRole('button', { name: 'Search', exact: true }).click();
  }

  test('picks a target (never the course itself) and merges', async ({ page }) => {
    await signIn(page, ADMIN);
    await search(page, 5, 'Elm Grove');
    await expect(page.locator('[data-candidate="Elm Grove"]')).toHaveCount(0);
    await page.locator('[data-candidate="Elm Grove Golf Club"]').click();
    await page.getByRole('button', { name: 'Merge course' }).click();
    await expect(page.locator('[data-merge-result]')).toContainText('Merged. 12 rounds and 0 open flags moved.');
    const post = (await adminCalls('POST', `/admin/courses/${course(5)}/merge`)).at(-1);
    expect(post?.body).toEqual({ intoCourseId: course(6) });
    await page.getByRole('link', { name: 'Open the course it was merged into' }).click();
    await expect(page.getByRole('heading', { name: 'Elm Grove Golf Club', level: 1 })).toBeVisible();
    await page.goto(`/admin/courses/${course(5)}`);
    await expect(page.locator('[data-merged]')).toBeVisible();
  });

  test('lists the tee versions that have no match, with the reason for each', async ({ page }) => {
    await signIn(page, ADMIN);
    await search(page, 7, 'Fern Valley');
    await page.locator('[data-candidate="Fern Valley Golf Club"]').click();
    const [response] = await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST'), page.getByRole('button', { name: 'Merge course' }).click()]);
    expect(response.status()).toBe(409);
    await expect(page.getByRole('alert').first()).toContainText('no match');
    const rows = page.locator('[data-unmatched] tbody tr');
    await expect(rows).toHaveCount(3);
    await expect(page.locator('tr[data-reason="NoMatchingVersion"]')).toContainText('no version with this effective date');
    await expect(page.locator('tr[data-reason="AmbiguousTee"]')).toContainText('more than one tee');
    await expect(page.locator('tr[data-reason="MissingHoles"]')).toContainText('lacks holes');
    await expect(page.locator('tr[data-reason="NoMatchingVersion"] td .num').first()).toHaveText('2020-01-01');
    // The target stays chosen so the admin can retry after fixing it.
    await expect(page.locator('[data-candidate="Fern Valley Golf Club"] input')).toBeChecked();
  });

  test('shows a busy 409 as a message', async ({ page }) => {
    await signIn(page, ADMIN);
    await search(page, 12, 'Kestrel Trails');
    await page.locator('[data-candidate="Kestrel Trails"]').click();
    await page.getByRole('button', { name: 'Merge course' }).click();
    await expect(page.getByRole('alert').first()).toContainText('Rounds kept being saved');
    await expect(page.locator('[data-unmatched]')).toHaveCount(0);
  });
});

test.describe('flags', () => {
  test('resolves an open flag', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto('/admin/flags');
    const note = 'Hole 4 par should be 5 (flag-resolve)';
    await page.getByRole('button', { name: `Resolve flag: ${note}` }).click();
    await expect(page.getByRole('status')).toContainText('Flag resolved.');
    await expect(page.locator(`tr[data-flag="${note}"]`)).toHaveCount(0);
    await page.getByRole('link', { name: 'Resolved' }).click();
    await expect(page.locator(`tr[data-flag="${note}"]`)).toBeVisible();
    expect((await adminCalls('POST', '/admin/flags/ad333333-0000-4000-8000-000000000001/resolve')).length).toBe(1);
  });

  test('dismisses an open flag', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto('/admin/flags');
    const note = 'Wrong phone number (flag-dismiss)';
    await page.getByRole('button', { name: `Dismiss flag: ${note}` }).click();
    await expect(page.getByRole('status')).toContainText('Flag dismissed.');
    await page.getByRole('link', { name: 'Dismissed' }).click();
    await expect(page.locator(`tr[data-flag="${note}"]`)).toBeVisible();
    expect((await adminCalls('POST', '/admin/flags/ad333333-0000-4000-8000-000000000002/dismiss')).length).toBe(1);
  });

  test('shows a 409 for a flag someone else already closed', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto('/admin/flags');
    await page.getByRole('button', { name: 'Resolve flag: Closed already (flag-conflict)' }).click();
    await expect(page.getByRole('alert')).toContainText('Flag not updated');
    await expect(page.getByRole('alert')).toContainText('already');
  });

  test('closed flags have no actions', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto('/admin/flags?status=Resolved');
    await expect(page.locator('tr[data-flag="Old report (flag-resolved-already)"]')).toBeVisible();
    await expect(page.getByRole('button', { name: /^(Resolve|Dismiss) flag/ })).toHaveCount(0);
  });
});

test.describe('settings', () => {
  test('saves the trial rounds', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto('/admin/settings');
    await page.getByLabel('Shot-by-shot rounds for Free golfers').fill('5');
    await page.getByRole('button', { name: 'Save settings' }).click();
    await expect(page.getByRole('status')).toContainText('Settings saved.');
    await expect(page.getByLabel('Shot-by-shot rounds for Free golfers')).toHaveValue('5');
    // (The next test also PUTs to settings, so look for this call rather than the last one.)
    expect((await adminCalls('PUT', '/admin/settings')).map((c) => c.body)).toContainEqual({ trialShotRounds: 5 });
  });

  test('shows the API error next to the field', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.goto('/admin/settings');
    const input = page.getByLabel('Shot-by-shot rounds for Free golfers');
    // The browser's own number input would block "-1"; post it the way a script would not be able to.
    await input.evaluate((el) => {
      (el as HTMLInputElement).type = 'text';
      (el.closest('form') as HTMLFormElement).noValidate = true;
    });
    await input.fill('-1');
    const [response] = await Promise.all([page.waitForResponse((r) => r.request().method() === 'POST'), page.getByRole('button', { name: 'Save settings' }).click()]);
    expect(response.status()).toBe(400);
    await expect(input).toHaveAttribute('aria-invalid', 'true');
    await expect(page.getByText('TrialShotRounds must be a whole number, 0 or more.')).toBeVisible();
  });
});
