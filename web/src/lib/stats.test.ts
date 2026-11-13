import { describe, expect, it } from 'vitest';
import { formatDecimal } from './format';
import { bucketLabel, firstProblem, yearsFrom, formatDate, formatShortDate, parseYear, pickYear, sectionHref, STATS_SECTIONS } from './stats';

describe('formatDecimal and dates', () => {
  it('formats handicap indexes to 1 dp with a real minus', () => {
    expect(formatDecimal(11.8)).toBe('11.8');
    expect(formatDecimal('12')).toBe('12.0');
    expect(formatDecimal(-1.24)).toBe('−1.2');
    expect(formatDecimal(-0.04)).toBe('0.0');
    expect(formatDecimal(null)).toBe('–');
  });

  it('formats calendar dates without a time-zone shift', () => {
    expect(formatDate('2025-06-14')).toBe('Jun 14, 2025');
    expect(formatShortDate('2025-01-01')).toBe('Jan 1');
  });
});

describe('firstProblem and yearsFrom', () => {
  const problem = { status: 502, message: 'Down', fieldErrors: {} };
  it('returns the first failure', () => {
    expect(firstProblem({ ok: true, status: 200, data: 1 }, { ok: false, problem })).toBe(problem);
    expect(firstProblem({ ok: true, status: 200, data: 1 })).toBeNull();
  });

  it('sorts years newest first and treats a failure as none', () => {
    expect(yearsFrom({ ok: true, status: 200, data: { years: [2023, '2025', 2024] } })).toEqual([2025, 2024, 2023]);
    expect(yearsFrom({ ok: false, problem })).toEqual([]);
  });
});

describe('bucketLabel', () => {
  it('labels open and closed buckets with an en dash', () => {
    expect(bucketLabel({ min: null, max: 39, count: 0 })).toBe('<40');
    expect(bucketLabel({ min: 40, max: 49, count: 0 })).toBe('40–49');
    expect(bucketLabel({ min: 100, max: null, count: 0 })).toBe('100+');
    expect(bucketLabel({ min: '80', max: '89', count: 0 })).toBe('80–89');
    expect(bucketLabel({ min: null, max: null, count: 0 })).toBe('All');
  });
});

describe('parseYear', () => {
  it('accepts four-digit years only', () => {
    expect(parseYear('2025')).toBe(2025);
    expect(parseYear('1900')).toBe(1900);
    expect(parseYear('25')).toBeNull();
    expect(parseYear('2025x')).toBeNull();
    expect(parseYear('0999')).toBeNull();
    expect(parseYear(undefined)).toBeNull();
    expect(parseYear(null)).toBeNull();
    expect(parseYear('99999')).toBeNull();
  });
});

describe('pickYear', () => {
  it('uses the requested year when it has rounds, otherwise the latest', () => {
    expect(pickYear([2025, 2024], '2024')).toBe(2024);
    expect(pickYear([2025, 2024], '2019')).toBe(2025);
    expect(pickYear([2024, 2025], null)).toBe(2025);
    expect(pickYear([], '2024')).toBeNull();
  });
});

describe('sectionHref', () => {
  it('puts year sections under the year and keeps the year on the career and handicap links', () => {
    expect(sectionHref('overall', 2025)).toBe('/stats/2025');
    expect(sectionHref('off-the-tee', 2025)).toBe('/stats/2025/off-the-tee');
    expect(sectionHref('career', 2025)).toBe('/stats/career?year=2025');
    expect(sectionHref('handicap', null)).toBe('/stats/handicap');
  });

  it('has a unique slug per section', () => {
    const slugs = STATS_SECTIONS.map((s) => s.slug);
    expect(new Set(slugs).size).toBe(slugs.length);
  });
});
