import { expect, test, type Page } from '@playwright/test';
import { DASH, formatAverage, formatDecimal, formatInteger, formatPercent, formatToPar } from '../../src/lib/format';
import { STATS_SECTIONS, formatDate, sectionHref } from '../../src/lib/stats';
import y from '../fixtures/stats-2025.json' with { type: 'json' };
import c from '../fixtures/stats-career.json' with { type: 'json' };
import { USER, signIn } from './support';

// Every number on the stats pages is the fake API's fixture value run through src/lib/format.ts.
const tile = (page: Page, label: string) => page.locator(`[data-stat="${label}"] .tile-value`);
const tileCaption = (page: Page, label: string) => page.locator(`[data-stat="${label}"] .tile-caption`);
/** The fallback data table of the chart titled `title`. */
const chartTable = (page: Page, title: string) => page.locator('figure[data-chart]').filter({ has: page.getByText(title, { exact: true }) }).locator('table');

test.beforeEach(async ({ page }) => {
  await signIn(page, USER);
});

test('/stats opens the overall section for the latest year', async ({ page }) => {
  await page.goto('/stats');
  await expect(page).toHaveURL(/\/stats\/2025$/);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Overall, 2025');
});

test('overall: tiles and charts show the API numbers', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto('/stats/2025');
  await expect(tile(page, 'Handicap index')).toHaveText(formatDecimal(y.overall.handicapIndex));
  await expect(tileCaption(page, 'Handicap index')).toHaveText(`As of ${formatDate(y.overall.handicapEffectiveOn)}`);
  await expect(tile(page, '18-hole average')).toHaveText(formatAverage(y.overall.scoringAverage18.value));
  await expect(tile(page, 'Holes played')).toHaveText(formatInteger(y.overall.holesPlayed));
  await expect(tile(page, 'To par')).toHaveText(formatToPar(y.overall.toPar));
  await expect(tile(page, 'To par')).toHaveText('+16');

  // Six charts, each drawn by the island, each with its data table still in the page.
  const figures = page.locator('figure[data-chart]');
  await expect(figures).toHaveCount(6);
  for (const f of await figures.all()) {
    await expect(f).toHaveAttribute('data-ready', 'true');
    await expect(f.locator('canvas')).toBeVisible();
    await expect(f.locator('table')).toHaveCount(1);
  }

  const drive = chartTable(page, 'Driving accuracy');
  await expect(drive.locator('tbody tr').nth(0)).toContainText(formatPercent(y.offTheTee.drivingAccuracy.value));
  await expect(drive.locator('tbody tr').nth(0)).toContainText('19/29');
  await expect(chartTable(page, 'Greens in regulation').locator('tbody tr').nth(3)).toContainText(formatPercent(y.approach.girFromRight.value));
  await expect(chartTable(page, 'Recovery').locator('tbody tr').nth(2)).toContainText('0.0%');
  await expect(chartTable(page, 'Putting averages').locator('tbody tr').nth(0)).toContainText(formatAverage(y.putting.puttsPerGir.value));
  const history = chartTable(page, 'Handicap history').locator('tbody tr');
  await expect(history).toHaveCount(c.handicap.entries.length);
  await expect(history.last()).toContainText('11.8');
  expect(errors).toEqual([]);
});

test('charts fall back to their data table without JavaScript', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await signIn(page, USER);
  await page.goto('/stats/2025/season');
  const figure = page.locator('figure[data-chart]').first();
  await expect(figure).not.toHaveAttribute('data-ready', /.*/);
  await expect(figure.locator('canvas')).toBeHidden();
  await expect(figure.locator('table')).toBeVisible();
  await expect(figure.locator('tbody tr').first()).toContainText('<40');
  await context.close();
});

test('season: counts and both histograms', async ({ page }) => {
  await page.goto('/stats/2025/season');
  await expect(tile(page, '9-hole rounds')).toHaveText(formatInteger(y.season.rounds9));
  await expect(tile(page, '18-hole rounds')).toHaveText(formatInteger(y.season.rounds18));
  await expect(tile(page, 'Other lengths')).toHaveText(formatInteger(y.season.roundsOtherLength));
  const nine = chartTable(page, '9-hole scores').locator('tbody tr');
  await expect(nine).toHaveText([/<40\s*1/, /40–49\s*0/, /50\+\s*0/]);
  const eighteen = chartTable(page, '18-hole scores').locator('tbody tr');
  await expect(eighteen).toHaveText([/<80\s*1/, /80–89\s*0/, /90–99\s*0/, /100\+\s*0/]);
});

test('results: one row per round, linking to its scorecard', async ({ page }) => {
  await page.goto('/stats/2025/results');
  const rows = page.locator('table.results tbody tr');
  await expect(rows).toHaveCount(y.results.rounds.length);
  for (const [i, r] of y.results.rounds.entries()) {
    const cells = rows.nth(i).locator('td');
    await expect(cells).toHaveText([
      `${r.courseName} (${r.teeName})`,
      formatInteger(r.holeCount),
      formatInteger(r.score),
      formatToPar(r.toPar),
      formatInteger(r.putts),
      formatPercent(r.gir.value),
      formatPercent(r.drivingAccuracy.value),
      formatToPar(r.par3ToPar),
      formatToPar(r.par4ToPar),
      formatToPar(r.par5ToPar),
    ]);
  }
  // A real minus and an en dash for "no par 5s".
  await expect(rows.nth(1).locator('td').nth(7)).toHaveText('−1');
  await expect(rows.nth(2).locator('td').nth(9)).toHaveText(DASH);
  await rows.nth(0).getByRole('link').click();
  await expect(page).toHaveURL(`/rounds/${y.results.rounds[0]!.roundId}`);
});

test('scoring', async ({ page }) => {
  await page.goto('/stats/2025/scoring');
  const s = y.scoring;
  await expect(tile(page, '18 holes')).toHaveText(formatAverage(s.scoringAverage18.value));
  await expect(tile(page, '9 holes')).toHaveText(formatAverage(s.scoringAverage9.value));
  await expect(tile(page, 'Low 18')).toHaveText(formatInteger(s.lowest18));
  await expect(tile(page, 'High 9')).toHaveText(formatInteger(s.highest9));
  await expect(tile(page, 'Birdies')).toHaveText(formatInteger(s.birdies));
  await expect(tile(page, 'Eagles')).toHaveText('0');
  await expect(tile(page, 'Par breakers')).toHaveText(formatPercent(s.parBreakers.value));
  await expect(tile(page, 'Birdie or better')).toHaveText(formatPercent(s.birdieOrBetter.value));
  await expect(tile(page, 'Par 3s')).toHaveText(formatAverage(s.par3Average.value));
  await expect(tile(page, 'Par 4s')).toHaveText(formatAverage(s.par4Average.value));
  await expect(tile(page, 'Par 5s')).toHaveText(formatAverage(s.par5Average.value));
});

test('off the tee', async ({ page }) => {
  await page.goto('/stats/2025/off-the-tee');
  const t = y.offTheTee;
  await expect(tile(page, 'Driving accuracy')).toHaveText(formatPercent(t.drivingAccuracy.value));
  await expect(tile(page, 'Missed left')).toHaveText(formatPercent(t.leftTendency.value));
  await expect(tile(page, 'Missed right')).toHaveText(formatPercent(t.rightTendency.value));
  const table = page.locator('.after-tee table tbody tr');
  await expect(table.nth(0).locator('td .stat')).toHaveText([t.par4ToParFromFairway, t.par4ToParFromLeft, t.par4ToParFromRight].map((a) => formatToPar(a.value)));
  await expect(table.nth(1).locator('td .stat')).toHaveText(['−0.20', DASH, '+1.50']);
});

test('approach and around the green', async ({ page }) => {
  await page.goto('/stats/2025/approach');
  const a = y.approach;
  for (const [label, r] of [
    ['GIR', a.gir],
    ['GIR from fairway', a.girFromFairway],
    ['GIR from left', a.girFromLeft],
    ['GIR from right', a.girFromRight],
    ['Sand saves', a.sandSave],
    ['Scrambling', a.scrambling],
    ['Hole-outs', a.holeOut],
  ] as const) {
    await expect(tile(page, label)).toHaveText(formatPercent(r.value));
    await expect(tileCaption(page, label)).toHaveText(`${r.numerator}/${r.denominator}`);
  }
});

test('putting', async ({ page }) => {
  await page.goto('/stats/2025/putting');
  const p = y.putting;
  await expect(tile(page, 'Putts per GIR')).toHaveText(formatAverage(p.puttsPerGir.value));
  await expect(tile(page, 'Putts per hole')).toHaveText(formatAverage(p.puttsPerHole.value));
  await expect(tile(page, 'Per 18-hole round')).toHaveText(formatAverage(p.puttsPerRound18.value));
  await expect(tile(page, 'Per 9-hole round')).toHaveText(formatAverage(p.puttsPerRound9.value));
  await expect(tile(page, 'One-putts')).toHaveText(formatPercent(p.onePutt.value));
  await expect(tile(page, 'Three-putts')).toHaveText(formatPercent(p.threePuttRate.value));
  await expect(tile(page, 'Birdie conversion')).toHaveText(formatPercent(p.birdieConversion.value));
});

test('courses: one row per course and tee, nulls as dashes', async ({ page }) => {
  await page.goto('/stats/2025/courses');
  const rows = page.locator('table.summary tbody tr');
  await expect(rows).toHaveCount(y.courses.courses.length);
  for (const [i, row] of y.courses.courses.entries()) {
    await expect(rows.nth(i).locator('th')).toContainText(row.courseName);
    await expect(rows.nth(i).locator('td')).toHaveText([
      formatInteger(row.eighteen.rounds),
      formatAverage(row.eighteen.average.value),
      formatInteger(row.eighteen.low),
      formatInteger(row.eighteen.high),
      formatInteger(row.nine.rounds),
      formatAverage(row.nine.average.value),
      formatInteger(row.nine.low),
      formatInteger(row.nine.high),
      formatInteger(row.holes),
      formatToPar(row.toPar),
      formatAverage(row.puttsPerHole.value),
      formatPercent(row.gir.value),
      formatPercent(row.drivingAccuracy.value),
      formatToPar(row.par3ToPar.value),
      formatToPar(row.par4ToPar.value),
      formatToPar(row.par5ToPar.value),
      formatDate(row.earliest),
      formatDate(row.latest),
    ]);
  }
  await expect(rows.nth(0).locator('td').nth(5)).toHaveText(DASH);
});

test('career: one row per year, linking to that season', async ({ page }) => {
  await page.goto('/stats/career?year=2024');
  const rows = page.locator('table.summary tbody tr');
  await expect(rows).toHaveCount(c.career.years.length);
  for (const [i, row] of c.career.years.entries()) {
    await expect(rows.nth(i).locator('td').nth(1)).toHaveText(formatAverage(row.eighteen.average.value));
    await expect(rows.nth(i).locator('td').nth(5)).toHaveText(formatAverage(row.nine.average.value));
    await expect(rows.nth(i).locator('td').nth(9)).toHaveText(formatToPar(row.toPar));
  }
  await expect(rows.nth(2).locator('td').nth(5)).toHaveText(DASH);
  // The year sections' tabs lead back to the year the golfer came from.
  await expect(page.getByRole('navigation', { name: 'Stats sections' }).getByRole('link', { name: 'Scoring' })).toHaveAttribute('href', '/stats/2024/scoring');
  await rows.nth(1).getByRole('link', { name: '2024' }).click();
  await expect(page).toHaveURL('/stats/2024');
});

test('handicap: table and chart', async ({ page }) => {
  await page.goto('/stats/handicap');
  await expect(tile(page, 'Current index')).toHaveText('11.8');
  const rows = page.locator('.history tbody tr');
  await expect(rows).toHaveCount(c.handicap.entries.length);
  const newestFirst = [...c.handicap.entries].reverse();
  for (const [i, e] of newestFirst.entries()) {
    await expect(rows.nth(i)).toHaveText(new RegExp(`${formatDate(e.effectiveOn)}\\s*${formatDecimal(e.index).replace('.', '\\.')}`));
  }
  await expect(page.locator('figure[data-chart]')).toHaveAttribute('data-ready', 'true');
});

test('the season picker keeps the section', async ({ page }) => {
  await page.goto('/stats/2025/putting');
  await page.getByLabel('Season').selectOption('2024');
  await page.getByRole('button', { name: 'Show' }).click();
  await expect(page).toHaveURL('/stats/2024/putting');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Putting, 2024');
});

test('section tabs mark the current section', async ({ page }) => {
  await page.goto('/stats/2025/approach');
  const tabs = page.getByRole('navigation', { name: 'Stats sections' }).getByRole('link');
  await expect(tabs).toHaveText(STATS_SECTIONS.map((s) => s.label));
  await expect(tabs.and(page.locator('[aria-current="page"]'))).toHaveText('Approach');
});

// A year with no rounds: every section renders, blank, without errors.
for (const s of STATS_SECTIONS.filter((x) => x.perYear)) {
  test(`empty year: ${s.label}`, async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (e) => errors.push(e.message));
    const res = await page.goto(sectionHref(s.slug, 2019));
    expect(res?.status()).toBe(200);
    await expect(page.locator('[data-empty-year="2019"]')).toBeVisible();
    await expect(page.locator('.problem')).toHaveCount(0);
    for (const f of await page.locator('figure[data-chart]').all()) await expect(f).toHaveAttribute('data-ready', 'true');
    expect(errors).toEqual([]);
  });
}

test('empty year: overall shows dashes and the handicap as of that year', async ({ page }) => {
  await page.goto('/stats/2019');
  await expect(tile(page, '18-hole average')).toHaveText(DASH);
  await expect(tile(page, 'Holes played')).toHaveText('0');
  await expect(tile(page, 'To par')).toHaveText('E');
  await expect(tile(page, 'Handicap index')).toHaveText(DASH);
  await page.goto('/stats/2023');
  await expect(tile(page, 'Handicap index')).toHaveText('13.6');
});

test('a year that is not a year is not found', async ({ page }) => {
  for (const path of ['/stats/abc', '/stats/20255', '/stats/abc/scoring']) {
    const res = await page.goto(path);
    expect(res?.status(), path).toBe(404);
  }
});
