import { describe, expect, it } from 'vitest';
import type { Schemas } from '../api/client';
import { scoreMark, subtotals } from './scorecard';

type Hole = Schemas['ScorecardHoleDto'];

function hole(number: number, par: number, score: number | null, putts = 2, seq = number): Hole {
  return {
    number,
    par,
    yardage: 300 + number,
    strokeIndex: number,
    result:
      score === null
        ? null
        : {
            entrySequence: seq,
            score,
            putts,
            fairwayHit: false,
            teeShotLeft: false,
            teeShotRight: false,
            teeShotLong: false,
            teeShotShort: false,
            gir: false,
            penaltyStrokes: 0,
            greensideSandShots: 0,
            fairwaySandShots: 0,
          },
  };
}

const range = (from: number, to: number) => Array.from({ length: to - from + 1 }, (_, i) => from + i);

describe('subtotals', () => {
  it('gives out, in and total when all 18 holes are played once', () => {
    const holes = range(1, 18).map((n) => hole(n, 4, 5));
    const t = subtotals(holes);
    expect(t.out).toEqual({ holes: 9, par: 36, yardage: range(1, 9).reduce((a, n) => a + 300 + n, 0), score: 45, putts: 18 });
    expect(t.in).toEqual({ holes: 9, par: 36, yardage: range(10, 18).reduce((a, n) => a + 300 + n, 0), score: 45, putts: 18 });
    expect(t.total).toMatchObject({ holes: 18, par: 72, score: 90, putts: 36 });
  });

  it('gives only out on a front nine; unplayed holes stay out of every total', () => {
    const holes = [...range(1, 9).map((n) => hole(n, 4, 4)), ...range(10, 18).map((n) => hole(n, 4, null))];
    const t = subtotals(holes);
    expect(t.out).toMatchObject({ holes: 9, score: 36 });
    expect(t.in).toBeNull();
    expect(t.total).toMatchObject({ holes: 9, par: 36, score: 36 });
  });

  it('drops the out subtotal when a front-nine hole is played twice', () => {
    const holes = [...range(1, 9).map((n) => hole(n, 4, 4)), hole(3, 4, 6, 3, 10), ...range(10, 18).map((n) => hole(n, 4, 4, 2, n + 1))];
    const t = subtotals(holes);
    expect(t.out).toBeNull();
    expect(t.in).toMatchObject({ holes: 9, score: 36 });
    expect(t.total).toMatchObject({ holes: 19, score: 78, putts: 39 });
  });

  it('drops the back subtotal when a back-nine hole is missing', () => {
    const holes = [...range(1, 17).map((n) => hole(n, 4, 4)), hole(18, 4, null)];
    expect(subtotals(holes).in).toBeNull();
    expect(subtotals(holes).out).not.toBeNull();
  });

  it('accepts numbers the API sent as strings', () => {
    const holes = range(1, 9).map((n) => ({ ...hole(n, 4, 5), par: '4' }));
    expect(subtotals(holes).out).toMatchObject({ par: 36, score: 45 });
  });

  it('has a zero total for a round with no played holes', () => {
    expect(subtotals([hole(1, 4, null)]).total).toEqual({ holes: 0, par: 0, yardage: 0, score: 0, putts: 0 });
  });
});

describe('scoreMark', () => {
  it.each([
    [2, 4, 'eagle'],
    [1, 4, 'eagle'],
    [3, 4, 'birdie'],
    [4, 4, 'par'],
    [5, 4, 'bogey'],
    [6, 4, 'double'],
    [9, 4, 'double'],
  ])('score %i on a par %i is %s', (score, par, mark) => {
    expect(scoreMark(score, par)).toBe(mark);
  });
});
