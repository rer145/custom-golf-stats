/**
 * The one place numbers are formatted for display. Render the results in a `.num` (JetBrains Mono, tabular figures).
 * The API's numeric fields are typed `number | string` by the OpenAPI document, so every formatter accepts both.
 */
export const DASH = '–';
export const MINUS = '−';

export type Numeric = number | string | null | undefined;

function toNumber(value: Numeric): number | null {
  if (value === null || value === undefined || value === '') return null;
  const n = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(n) ? n : null;
}

/** Fixed decimals, real minus sign, and no "−0.0". */
function fixed(n: number, digits: number): string {
  const s = Math.abs(n).toFixed(digits);
  const isZero = Number(s) === 0;
  return n < 0 && !isZero ? `${MINUS}${s}` : s;
}

/** `54.3%` (1 dp). */
export function formatPercent(value: Numeric): string {
  const n = toNumber(value);
  return n === null ? DASH : `${fixed(n, 1)}%`;
}

/** `1.80` (2 dp). */
export function formatAverage(value: Numeric): string {
  const n = toNumber(value);
  return n === null ? DASH : fixed(n, 2);
}

/** Whole numbers, rounded. */
export function formatInteger(value: Numeric): string {
  const n = toNumber(value);
  return n === null ? DASH : fixed(Math.round(n), 0);
}

/** `E`, `+3`, `−2`; fractional values (averages to par) to 2 dp: `+2.50`. */
export function formatToPar(value: Numeric): string {
  const n = toNumber(value);
  if (n === null) return DASH;
  const digits = Number.isInteger(n) ? 0 : 2;
  const s = fixed(n, digits);
  if (Number(s.replace(MINUS, '-')) === 0) return 'E';
  return n > 0 ? `+${s}` : s;
}

/** Always shows the sign: `+1.1`, `−0.4`, `0.0`. */
export function formatSigned(value: Numeric, digits = 1): string {
  const n = toNumber(value);
  if (n === null) return DASH;
  const s = fixed(n, digits);
  if (Number(s.replace(MINUS, '-')) === 0) return (0).toFixed(digits);
  return n > 0 ? `+${s}` : s;
}

/** Plain decimals, sign only when negative (a real minus): handicap indexes `11.8`, `−1.2`. */
export function formatDecimal(value: Numeric, digits = 1): string {
  const n = toNumber(value);
  return n === null ? DASH : fixed(n, digits);
}
