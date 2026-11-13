import { describe, expect, it } from 'vitest';
import { formatValue, ratioChartValue } from './chart-spec';
import { threePuttAvoidance } from './stats';

describe('ratioChartValue', () => {
  it('plots an API fraction as a percentage', () => {
    expect(ratioChartValue({ value: 0.25 })).toBe(25);
    expect(ratioChartValue({ value: '0.5' })).toBe(50);
    expect(ratioChartValue({ value: null })).toBeNull();
    expect(formatValue('percent', ratioChartValue({ value: 5 / 37 }))).toBe('13.5%');
  });
});

describe('threePuttAvoidance', () => {
  it('is 1 − the three-putt rate, with the counts behind it', () => {
    expect(threePuttAvoidance({ value: 3 / 37, numerator: 3, denominator: 37 })).toEqual({ value: 34 / 37, numerator: 34, denominator: 37 });
    expect(threePuttAvoidance({ value: null, numerator: 0, denominator: 0 })).toEqual({ value: null, numerator: 0, denominator: 0 });
  });
});
