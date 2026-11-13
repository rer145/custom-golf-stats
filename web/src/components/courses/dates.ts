import { DASH } from '../../lib/format';
import { DEFAULT_EFFECTIVE_FROM } from './course-form';

const dateFormat = new Intl.DateTimeFormat('en-US', { year: 'numeric', month: 'short', day: 'numeric', timeZone: 'UTC' });

/** `2026-04-01` or an ISO timestamp → `Apr 1, 2026` (UTC, so a date-only value never shifts a day). */
export function formatDate(value: string | null | undefined): string {
  if (!value) return DASH;
  const date = new Date(/^\d{4}-\d{2}-\d{2}$/.test(value) ? `${value}T00:00:00Z` : value);
  return Number.isNaN(date.getTime()) ? DASH : dateFormat.format(date);
}

/** A tee version's start: the default first-version date reads as "Original". */
export function formatEffectiveFrom(value: string): string {
  return value === DEFAULT_EFFECTIVE_FROM ? 'Original' : formatDate(value);
}

/** Today as `YYYY-MM-DD` (UTC), for date inputs' defaults. */
export function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}
