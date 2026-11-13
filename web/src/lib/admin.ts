/** Helpers for the /admin pages: form number parsing, display bits, and the 409 problem extensions. */
import { type ApiProblem } from '../api/client';
import { DASH } from './format';

export const ADMIN_SECTIONS = [
  { href: '/admin', label: 'Overview' },
  { href: '/admin/users', label: 'Users' },
  { href: '/admin/courses', label: 'Courses' },
  { href: '/admin/flags', label: 'Flags' },
  { href: '/admin/settings', label: 'Settings' },
] as const;

export type Parsed = { value: number | null } | { error: string };

function parse(text: string, whole: boolean): Parsed {
  const t = text.trim();
  if (t === '') return { value: null };
  const n = Number(t);
  if (!Number.isFinite(n)) return { error: 'Enter a number.' };
  if (whole && !Number.isInteger(n)) return { error: 'Enter a whole number.' };
  return { value: n };
}

/** Blank gives `null`; otherwise a finite number or an error message. */
export const parseDecimal = (text: string): Parsed => parse(text, false);
export const parseWholeNumber = (text: string): Parsed => parse(text, true);

/** `2026-11-01T09:30:00+00:00` becomes `2026-11-01`. */
export function isoDate(value: string | null | undefined): string {
  return value ? value.slice(0, 10) : DASH;
}

export interface UnmatchedVersion {
  teeId: string;
  teeName: string;
  gender: string;
  versionId: string;
  effectiveFrom: string;
  rounds: number;
  reason: string;
}

/** The merge 409's `unmatched` list (entries that aren't shaped like one are dropped). */
export function unmatchedVersions(problem: ApiProblem | null | undefined): UnmatchedVersion[] {
  const raw = problem?.extensions?.unmatched;
  if (!Array.isArray(raw)) return [];
  const out: UnmatchedVersion[] = [];
  for (const item of raw) {
    if (typeof item !== 'object' || item === null) continue;
    const r = item as Record<string, unknown>;
    if (typeof r.teeName !== 'string' || typeof r.reason !== 'string') continue;
    out.push({
      teeId: String(r.teeId ?? ''),
      teeName: r.teeName,
      gender: String(r.gender ?? ''),
      versionId: String(r.versionId ?? ''),
      effectiveFrom: String(r.effectiveFrom ?? ''),
      rounds: Number(r.rounds ?? 0),
      reason: r.reason,
    });
  }
  return out;
}

/** The version-correction 409's `holeNumbers`: holes rounds have played that the change would remove. */
export function playedHoleNumbers(problem: ApiProblem | null | undefined): number[] {
  const raw = problem?.extensions?.holeNumbers;
  return Array.isArray(raw) ? raw.filter((n): n is number => typeof n === 'number') : [];
}

const REASONS: Record<string, string> = {
  NoMatchingTee: 'The target has no tee with this name and gender.',
  AmbiguousTee: 'The target has more than one tee with this name and gender.',
  NoMatchingVersion: 'The matching tee has no version with this effective date.',
  MissingHoles: 'The matching version lacks holes these rounds played.',
};

export function unmatchedReason(reason: string): string {
  return REASONS[reason] ?? reason;
}
