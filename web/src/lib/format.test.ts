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
