import { describe, expect, it } from 'vitest';
import { parseHandicapIndex } from './handicap';

describe('parseHandicapIndex', () => {
  it('reads ordinary indexes', () => {
    expect(parseHandicapIndex(' 12.4 ')).toEqual({ value: 12.4 });
    expect(parseHandicapIndex('0')).toEqual({ value: 0 });
    expect(parseHandicapIndex('.5')).toEqual({ value: 0.5 });
  });

  it('reads a plus handicap as a negative index', () => {
    expect(parseHandicapIndex('+1.2')).toEqual({ value: -1.2 });
    expect(parseHandicapIndex('-1.2')).toEqual({ value: -1.2 });
    expect(parseHandicapIndex('−3')).toEqual({ value: -3 });
  });

  it('explains what is wrong', () => {
    expect(parseHandicapIndex('').error).toBe('A handicap index is required.');
    expect(parseHandicapIndex('scratch').error).toMatch(/Enter a number/);
    expect(parseHandicapIndex('1.2.3').value).toBeNull();
    expect(parseHandicapIndex('+10.0')).toEqual({ value: -10 });
    expect(parseHandicapIndex('+10.1')).toEqual({ value: null, error: 'A plus handicap can be at most +10.0.' });
    expect(parseHandicapIndex('-12').error).toBe('A plus handicap can be at most +10.0.');
  });
});
