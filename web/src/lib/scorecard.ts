import type { Schemas } from '../api/client';

type Hole = Schemas['ScorecardHoleDto'];

export interface Subtotal {
  holes: number;
  par: number;
  yardage: number;
  score: number;
  putts: number;
}

export interface Subtotals {
  /** Holes 1–9, only when each of them was played exactly once. */
  out: Subtotal | null;
  /** Holes 10–18, only when each of them was played exactly once. */
  in: Subtotal | null;
  /** Every played row (a hole played twice counts twice); unplayed holes are left out. */
  total: Subtotal;
}

const n = (v: number | string): number => (typeof v === 'number' ? v : Number(v));

function sum(rows: Hole[]): Subtotal {
  const t: Subtotal = { holes: 0, par: 0, yardage: 0, score: 0, putts: 0 };
  for (const h of rows) {
    if (!h.result) continue;
    t.holes += 1;
    t.par += n(h.par);
    t.yardage += n(h.yardage);
    t.score += n(h.result.score);
    t.putts += n(h.result.putts);
  }
  return t;
}

/** The played rows for holes from..to, when every one of those holes was played exactly once; otherwise null. */
function nine(rows: Hole[], from: number): Subtotal | null {
  const played = rows.filter((h) => h.result && n(h.number) >= from && n(h.number) <= from + 8);
  const numbers = new Set(played.map((h) => n(h.number)));
  return played.length === 9 && numbers.size === 9 ? sum(played) : null;
}

/** Out / In / Total for a scorecard's rows (in the API's order). */
export function subtotals(rows: Hole[]): Subtotals {
  return { out: nine(rows, 1), in: nine(rows, 10), total: sum(rows) };
}

export type ScoreMark = 'eagle' | 'birdie' | 'par' | 'bogey' | 'double';

/** How a hole's score is marked on the card: circles under par, squares over. */
export function scoreMark(score: number | string, par: number | string): ScoreMark {
  const d = n(score) - n(par);
  return d <= -2 ? 'eagle' : d === -1 ? 'birdie' : d === 0 ? 'par' : d === 1 ? 'bogey' : 'double';
}
