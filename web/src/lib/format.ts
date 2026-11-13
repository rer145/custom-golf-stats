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

/** One decimal place, no forced sign: course ratings (`72.1`), distances, `−1.2`. */
export function formatRating(value: Numeric): string {
  const n = toNumber(value);
  return n === null ? DASH : fixed(n, 1);
}

/** An API ratio (`value` is a 0–1 fraction, null with a zero denominator) as a percentage: `13.5%`. */
export function formatRatio(ratio: { value: Numeric } | null | undefined): string {
  const n = toNumber(ratio?.value);
  return n === null ? DASH : formatPercent(n * 100);
}

/** A handicap index to 1 dp. The API stores a plus handicap as a negative index, written `+2.1`. */
export function formatHandicap(value: Numeric): string {
  const n = toNumber(value);
  if (n === null) return DASH;
  const s = Math.abs(n).toFixed(1);
  return n < 0 && Number(s) !== 0 ? `+${s}` : s;
}

const dateFormat = new Intl.DateTimeFormat('en-US', { year: 'numeric', month: 'short', day: 'numeric', timeZone: 'UTC' });

/** `2026-04-01` or an ISO timestamp → `Apr 1, 2026` (UTC, so a date-only value never shifts a day). {@link DASH} for null or unparsable input. */
export function formatDate(value: string | null | undefined): string {
  if (!value) return DASH;
  const date = new Date(/^\d{4}-\d{2}-\d{2}$/.test(value) ? `${value}T00:00:00Z` : value);
  return Number.isNaN(date.getTime()) ? DASH : dateFormat.format(date);
}
