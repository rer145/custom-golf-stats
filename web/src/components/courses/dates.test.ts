import { describe, expect, it } from 'vitest';
import { formatEffectiveFrom, todayIso } from './dates';

describe('course dates', () => {
  it('shows the default first version date as Original', () => {
    expect(formatEffectiveFrom('1900-01-01')).toBe('Original');
    expect(formatEffectiveFrom('2026-04-01')).toBe('Apr 1, 2026');
  });

  it('today is the local date, not the UTC one', () => {
    expect(todayIso(new Date(2026, 0, 2, 23, 59))).toBe('2026-01-02');
    expect(todayIso(new Date(2026, 11, 31, 0, 1))).toBe('2026-12-31');
  });
});
