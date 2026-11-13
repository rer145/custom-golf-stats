import { describe, expect, it } from 'vitest';
import { DASH, MINUS, formatAverage, formatInteger, formatPercent, formatSigned, formatToPar } from './format';

describe('format', () => {
  it('uses an en dash for missing values and a real minus sign', () => {
    expect(DASH).toBe('–');
    expect(MINUS).toBe('−');
    for (const f of [formatAverage, formatInteger, formatPercent, formatSigned, formatToPar]) {
      expect(f(null)).toBe(DASH);
      expect(f(undefined)).toBe(DASH);
      expect(f(Number.NaN)).toBe(DASH);
    }
  });

  it('percentages to 1 dp', () => {
    expect(formatPercent(0)).toBe('0.0%');
    expect(formatPercent(54.25)).toBe('54.3%');
    expect(formatPercent(100)).toBe('100.0%');
    expect(formatPercent(-2.5)).toBe('−2.5%');
  });

  it('averages to 2 dp', () => {
    expect(formatAverage(1.8)).toBe('1.80');
    expect(formatAverage(31.456)).toBe('31.46');
    expect(formatAverage(-0.5)).toBe('−0.50');
    expect(formatAverage(-0.001)).toBe('0.00');
  });

  it('integers', () => {
    expect(formatInteger(72)).toBe('72');
    expect(formatInteger(-3)).toBe('−3');
    expect(formatInteger(1234)).toBe('1234');
  });

  it('to-par as E, +n, −n', () => {
    expect(formatToPar(0)).toBe('E');
    expect(formatToPar(3)).toBe('+3');
    expect(formatToPar(-2)).toBe('−2');
    expect(formatToPar(2.5)).toBe('+2.50');
    expect(formatToPar(-0.25)).toBe('−0.25');
    expect(formatToPar(0.001)).toBe('E');
  });

  it('signed numbers always show the sign', () => {
    expect(formatSigned(1.14)).toBe('+1.1');
    expect(formatSigned(-0.4)).toBe('−0.4');
    expect(formatSigned(0)).toBe('0.0');
    expect(formatSigned(-0.04)).toBe('0.0');
    expect(formatSigned(2, 0)).toBe('+2');
  });
});

describe('formatRating, formatHandicap and formatDate', () => {
  it('ratings to 1 dp', async () => {
    const { formatRating } = await import('./format');
    expect(formatRating(null)).toBe(DASH);
    expect(formatRating(72.1)).toBe('72.1');
    expect(formatRating('69')).toBe('69.0');
    expect(formatRating(-1.25)).toBe('−1.3');
  });

  it('handicap indexes: below zero is a plus handicap', async () => {
    const { formatHandicap } = await import('./format');
    expect(formatHandicap(null)).toBe(DASH);
    expect(formatHandicap(12.4)).toBe('12.4');
    expect(formatHandicap('0')).toBe('0.0');
    expect(formatHandicap(-2.1)).toBe('+2.1');
    expect(formatHandicap(-0.01)).toBe('0.0');
  });

  it('dates in UTC, DASH for null or bad input', async () => {
    const { formatDate } = await import('./format');
    expect(formatDate('2026-04-01')).toBe('Apr 1, 2026');
    expect(formatDate('2026-04-03T23:30:00Z')).toBe('Apr 3, 2026');
    expect(formatDate(null)).toBe(DASH);
    expect(formatDate('nope')).toBe(DASH);
  });
});
