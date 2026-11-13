import { describe, expect, it } from 'vitest';
import { DASH } from '../../lib/format';
import { formatDate, formatEffectiveFrom, todayIso } from './dates';

describe('course dates', () => {
  it('formats dates in UTC', () => {
    expect(formatDate('2026-04-01')).toBe('Apr 1, 2026');
    expect(formatDate('2026-04-03T23:30:00Z')).toBe('Apr 3, 2026');
    expect(formatDate(null)).toBe(DASH);
    expect(formatDate('nope')).toBe(DASH);
  });

  it('shows the default first version date as Original', () => {
    expect(formatEffectiveFrom('1900-01-01')).toBe('Original');
    expect(formatEffectiveFrom('2026-04-01')).toBe('Apr 1, 2026');
  });

  it('today is the local date, not the UTC one', () => {
    expect(todayIso(new Date(2026, 0, 2, 23, 59))).toBe('2026-01-02');
    expect(todayIso(new Date(2026, 11, 31, 0, 1))).toBe('2026-12-31');
  });
});
