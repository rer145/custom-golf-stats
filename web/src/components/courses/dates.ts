import { formatDate } from '../../lib/format';
import { DEFAULT_EFFECTIVE_FROM } from './course-form';

/** A tee version's start: the default first-version date reads as "Original". */
export function formatEffectiveFrom(value: string): string {
  return value === DEFAULT_EFFECTIVE_FROM ? 'Original' : formatDate(value);
}

/**
 * Today as `YYYY-MM-DD` in the server's local time zone (`TZ`), for date inputs' defaults and "as of today" checks.
 * The server can't know the golfer's zone, so pages that show it as a default also correct it to the browser's
 * local date with a small script (see the profile page); this is the fallback without script.
 */
export function todayIso(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}
