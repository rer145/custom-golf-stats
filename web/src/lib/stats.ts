import type { ApiProblem, ApiResult, Schemas } from '../api/client';

/** The first failed call's problem, or null when every call succeeded. */
export function firstProblem(...results: ApiResult<unknown>[]): ApiProblem | null {
  for (const r of results) if (!r.ok) return r.problem;
  return null;
}

/** The years a golfer has rounds in (newest first), or [] when the call failed. */
export function yearsFrom(result: ApiResult<Schemas['StatsYearsResponse']>): number[] {
  return result.ok ? result.data.years.map(Number).sort((a, b) => b - a) : [];
}

export type SectionSlug =
  | 'overall'
  | 'season'
  | 'results'
  | 'scoring'
  | 'off-the-tee'
  | 'approach'
  | 'putting'
  | 'courses'
  | 'career'
  | 'handicap';

export interface StatsSection {
  slug: SectionSlug;
  label: string;
  /** Year sections live under /stats/{year}; career and handicap cover every year. */
  perYear: boolean;
}

/** The stats sections, in tab order. */
export const STATS_SECTIONS: readonly StatsSection[] = [
  { slug: 'overall', label: 'Overall', perYear: true },
  { slug: 'season', label: 'Season', perYear: true },
  { slug: 'results', label: 'Results', perYear: true },
  { slug: 'scoring', label: 'Scoring', perYear: true },
  { slug: 'off-the-tee', label: 'Off the tee', perYear: true },
  { slug: 'approach', label: 'Approach', perYear: true },
  { slug: 'putting', label: 'Putting', perYear: true },
  { slug: 'courses', label: 'Courses', perYear: true },
  { slug: 'career', label: 'Career', perYear: false },
  { slug: 'handicap', label: 'Handicap', perYear: false },
];

export function isSectionSlug(value: string | null | undefined): value is SectionSlug {
  return STATS_SECTIONS.some((s) => s.slug === value);
}

/** The URL of a section. `year` is kept on the all-years sections so their tabs can lead back to it. */
export function sectionHref(slug: SectionSlug, year: number | null): string {
  const section = STATS_SECTIONS.find((s) => s.slug === slug)!;
  if (!section.perYear) return year === null ? `/stats/${slug}` : `/stats/${slug}?year=${year}`;
  return slug === 'overall' ? `/stats/${year}` : `/stats/${year}/${slug}`;
}

/** A four-digit year from a route or query value, or null. */
export function parseYear(value: string | null | undefined): number | null {
  if (!value || !/^[12]\d{3}$/.test(value)) return null;
  return Number(value);
}

/** The requested year if the golfer has rounds in it, otherwise their latest year; null when they have none. */
export function pickYear(years: readonly number[], requested: string | null | undefined): number | null {
  if (years.length === 0) return null;
  const wanted = parseYear(requested);
  if (wanted !== null && years.includes(wanted)) return wanted;
  return Math.max(...years);
}

/** `<40`, `40–49`, `100+`: a score histogram bucket's label. */
export function bucketLabel(bucket: Schemas['ScoreBucketDto']): string {
  const min = bucket.min === null ? null : Number(bucket.min);
  const max = bucket.max === null ? null : Number(bucket.max);
  if (min === null && max === null) return 'All';
  if (min === null) return `<${max! + 1}`;
  if (max === null) return `${min}+`;
  return `${min}–${max}`;
}

/** A ratio or average's value, or null (a zero denominator or count). */
export const valueOf = (x: { value: null | number | string } | null | undefined): number | string | null => x?.value ?? null;

/** `5/14`: the counts behind a ratio, for a caption. */
export function ratioCounts(r: Schemas['RatioDto']): string {
  return `${r.numerator}/${r.denominator}`;
}

/** `2025-06-14` → `Jun 14, 2025` (dates are calendar dates: no time zone shift). */
export function formatDate(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  if (!y || !m || !d) return iso;
  return new Date(Date.UTC(y, m - 1, d)).toLocaleDateString('en-US', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });
}

/** `2025-06-14` → `Jun 14` (within a year's table). */
export function formatShortDate(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  if (!y || !m || !d) return iso;
  return new Date(Date.UTC(y, m - 1, d)).toLocaleDateString('en-US', { day: 'numeric', month: 'short', timeZone: 'UTC' });
}
