import { describe, expect, it } from 'vitest';
import { formatHandicap, formatRatio } from './format';

// The API's RatioDto.value is a 0–1 fraction (Numerator / Denominator), null when the denominator is zero.
describe('formatRatio', () => {
  it('shows a fraction as a percentage to 1 dp', () => {
    expect(formatRatio({ value: 5 / 37 })).toBe('13.5%');
    expect(formatRatio({ value: 0.655172 })).toBe('65.5%');
    expect(formatRatio({ value: 1 })).toBe('100.0%');
    expect(formatRatio({ value: 0 })).toBe('0.0%');
    expect(formatRatio({ value: '0.25' })).toBe('25.0%');
  });

  it('shows a dash for no data', () => {
    expect(formatRatio({ value: null })).toBe('–');
    expect(formatRatio(null)).toBe('–');
    expect(formatRatio(undefined)).toBe('–');
  });
});

// The API stores a plus handicap as a negative index.
describe('formatHandicap', () => {
  it('writes plus handicaps with a plus sign and others plainly, to 1 dp', () => {
    expect(formatHandicap(12.4)).toBe('12.4');
    expect(formatHandicap('11.8')).toBe('11.8');
    expect(formatHandicap(-2.1)).toBe('+2.1');
    expect(formatHandicap(0)).toBe('0.0');
    expect(formatHandicap(-0.04)).toBe('0.0');
    expect(formatHandicap(null)).toBe('–');
  });
});
