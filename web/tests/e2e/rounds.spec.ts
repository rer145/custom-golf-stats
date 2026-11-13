import { expect, test } from '@playwright/test';
import { DASH, formatInteger, formatToPar } from '../../src/lib/format';
import { formatDate } from '../../src/lib/format';
import list from '../fixtures/rounds-list.json' with { type: 'json' };
import cards from '../fixtures/rounds-scorecards.json' with { type: 'json' };
import { USER, signIn } from './support';

const [full, frontNine, replayed] = cards.scorecards as [
  (typeof cards.scorecards)[number],
  (typeof cards.scorecards)[number],
  (typeof cards.scorecards)[number],
];

test.beforeEach(async ({ page }) => {
  await signIn(page, USER);
});

test.describe('round list', () => {
  test('defaults to the latest season, newest first', async ({ page }) => {
    await page.goto('/rounds');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Rounds, 2025');
    const rows = page.locator('table.rounds tbody tr');
    const in2025 = list.rounds.filter((r) => r.playedOn.startsWith('2025'));
    await expect(rows).toHaveCount(in2025.length);
    for (const [i, r] of in2025.entries()) {
      await expect(rows.nth(i).locator('th')).toHaveText(formatDate(r.playedOn));
      await expect(rows.nth(i).locator('td')).toHaveText([`${r.courseName} (${r.teeName})`, formatInteger(r.holes), formatInteger(r.score), formatToPar(r.toPar), 'Quick']);
    }
  });

  test('filters by season, or shows every season', async ({ page }) => {
    await page.goto('/rounds');
    await page.getByLabel('Season').selectOption('2024');
    await page.getByRole('button', { name: 'Show' }).click();
    await expect(page).toHaveURL('/rounds?year=2024');
    await expect(page.locator('table.rounds tbody tr')).toHaveCount(1);
    await expect(page.locator('table.rounds tbody tr td').last()).toHaveText('Shot by shot');

    await page.getByLabel('Season').selectOption('all');
    await page.getByRole('button', { name: 'Show' }).click();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('All rounds');
    await expect(page.locator('table.rounds tbody tr')).toHaveCount(list.rounds.length);
  });

  test('a season with no rounds says so', async ({ page }) => {
    await page.goto('/rounds?year=2019');
    await expect(page.getByText('No rounds in 2019.')).toBeVisible();
    await expect(page.locator('table.rounds')).toHaveCount(0);
  });

  test('a row opens its scorecard', async ({ page }) => {
    await page.goto('/rounds');
    await page.locator('table.rounds tbody tr').first().getByRole('link').click();
    await expect(page).toHaveURL(`/rounds/${full.roundId}`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(full.courseName);
  });
});

test.describe('scorecard', () => {
  test('an 18-hole round: every hole, Out, In and Total', async ({ page }) => {
    await page.goto(`/rounds/${full.roundId}`);
    await expect(page.locator('.round-numbers')).toContainText(formatInteger(full.score));
    await expect(page.locator('.round-numbers .to-par')).toHaveText(formatToPar(full.toPar));
    const rows = page.locator('table.scorecard > tbody:not(.unplayed) tr');
    await expect(rows).toHaveCount(18);
    await expect(rows.locator('th')).toHaveText(full.holes.map((h) => String(h.number)));
    await expect(rows.nth(2).locator('td').nth(3)).toHaveText(String(full.holes[2]!.result!.score));
    await expect(page.locator('table.scorecard tbody.unplayed')).toHaveCount(0);

    const front = full.holes.filter((h) => h.number <= 9);
    const back = full.holes.filter((h) => h.number > 9);
    const sum = (hs: typeof full.holes, f: (h: (typeof full.holes)[number]) => number) => hs.reduce((a, h) => a + f(h), 0);
    const out = page.locator('tr[data-subtotal="Out"] td');
    await expect(out.nth(0)).toHaveText(String(sum(front, (h) => h.par)));
    await expect(out.nth(3)).toHaveText(String(sum(front, (h) => h.result!.score)));
    await expect(page.locator('tr[data-subtotal="In"] td').nth(3)).toHaveText(String(sum(back, (h) => h.result!.score)));
    const total = page.locator('tr[data-subtotal="Total"] td');
    await expect(total.nth(0)).toHaveText(String(full.par));
    await expect(total.nth(3)).toHaveText(formatInteger(full.score));
    await expect(total.nth(4)).toHaveText(formatInteger(full.putts));
    await expect(total.nth(5)).toHaveText(formatToPar(full.toPar));
  });

  test('a nine-hole round: Out only, the back nine listed as not played', async ({ page }) => {
    await page.goto(`/rounds/${frontNine.roundId}`);
    await expect(page.locator('table.scorecard > tbody:not(.unplayed) tr')).toHaveCount(9);
    const unplayed = page.locator('tr[data-unplayed]');
    await expect(unplayed).toHaveCount(9);
    await expect(unplayed.locator('th')).toHaveText(['10', '11', '12', '13', '14', '15', '16', '17', '18']);
    await expect(unplayed.first().locator('td').nth(3)).toHaveText(DASH);
    await expect(page.locator('tr[data-subtotal="Out"]')).toHaveCount(1);
    await expect(page.locator('tr[data-subtotal="In"]')).toHaveCount(0);
    await expect(page.locator('tr[data-subtotal="Total"] td').nth(3)).toHaveText(formatInteger(frontNine.score));
  });

  test('a hole played twice: two rows in play order, and no Out subtotal', async ({ page }) => {
    await page.goto(`/rounds/${replayed.roundId}`);
    const rows = page.locator('table.scorecard > tbody:not(.unplayed) tr');
    await expect(rows).toHaveCount(10);
    await expect(rows.locator('th')).toHaveText([...[1, 2, 3, 4, 5, 6, 7, 8, 9].map((n) => new RegExp(`^\\s*${n}\\s*$`)), /^\s*1\s+again\s*$/]);
    await expect(rows.nth(9)).toHaveAttribute('data-again', 'true');
    await expect(rows.nth(0).locator('td').nth(3)).toHaveText(String(replayed.holes[0]!.result!.score));
    await expect(rows.nth(9).locator('td').nth(3)).toHaveText(String(replayed.holes[9]!.result!.score));
    await expect(page.locator('tr[data-subtotal="Out"]')).toHaveCount(0);
    await expect(page.locator('tr[data-subtotal="In"]')).toHaveCount(0);
    const total = page.locator('tr[data-subtotal="Total"] td');
    await expect(total.nth(3)).toHaveText(formatInteger(replayed.score));
    await expect(total.nth(5)).toHaveText(formatToPar(replayed.toPar));
    await expect(page.locator('tr[data-unplayed]')).toHaveCount(9);
  });

  test('an unknown round or a malformed id is not found', async ({ page }) => {
    for (const path of ['/rounds/66666666-6666-4666-8666-666666666666', '/rounds/not-a-round']) {
      const res = await page.goto(path);
      expect(res?.status(), path).toBe(404);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText('Out of bounds');
    }
  });
});
