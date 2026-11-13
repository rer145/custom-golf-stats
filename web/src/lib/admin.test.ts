import { describe, expect, it } from 'vitest';
import { toProblem } from '../api/client';
import { ADMIN_SECTIONS, isoDate, localProblem, pageStatus, parseDecimal, parseWholeNumber, playedHoleNumbers, unmatchedReason, unmatchedVersions } from './admin';

describe('parseDecimal / parseWholeNumber', () => {
  it('treats blank as null and parses numbers', () => {
    expect(parseDecimal('')).toEqual({ value: null });
    expect(parseDecimal(' 71.4 ')).toEqual({ value: 71.4 });
    expect(parseWholeNumber('130')).toEqual({ value: 130 });
  });
  it('rejects text and fractions where whole numbers are needed', () => {
    expect(parseDecimal('abc')).toEqual({ error: 'Enter a number.' });
    expect(parseWholeNumber('12.5')).toEqual({ error: 'Enter a whole number.' });
    expect(parseDecimal('1e999')).toEqual({ error: 'Enter a number.' });
  });
});

describe('isoDate', () => {
  it('keeps the date part of a timestamp', () => {
    expect(isoDate('2026-11-01T09:30:00+00:00')).toBe('2026-11-01');
    expect(isoDate(null)).toBe('–');
  });
});

describe('localProblem', () => {
  it('builds a 400 whose field errors show next to the fields', () => {
    const p = localProblem({ rating: ['Enter a number.'] });
    expect(p.status).toBe(400);
    expect(p.fieldErrors).toEqual({ rating: ['Enter a number.'] });
  });
});

describe('pageStatus', () => {
  it.each([
    [400, 400],
    [403, 403],
    [404, 404],
    [409, 409],
    [503, 503],
    [500, 502],
    [502, 502],
  ])('API %i shows as %i', (api, shown) => {
    expect(pageStatus(api)).toBe(shown);
  });
});

describe('409 extensions', () => {
  it('reads the unmatched list and played hole numbers', () => {
    const entry = { teeId: 't', teeName: 'Blue', gender: 'Male', versionId: 'v', effectiveFrom: '2020-01-01', rounds: 3, reason: 'AmbiguousTee' };
    const p = toProblem(409, { title: 'Tee versions do not match', unmatched: [entry], holeNumbers: [4, 5] });
    expect(unmatchedVersions(p)).toEqual([entry]);
    expect(playedHoleNumbers(p)).toEqual([4, 5]);
  });
  it('ignores junk', () => {
    const p = toProblem(409, { unmatched: [{ nope: 1 }, 'x'], holeNumbers: ['a', 2] });
    expect(unmatchedVersions(p)).toEqual([]);
    expect(playedHoleNumbers(p)).toEqual([2]);
    expect(unmatchedVersions(null)).toEqual([]);
  });
  it('explains every reason the API can give', () => {
    for (const r of ['NoMatchingTee', 'NoMatchingVersion', 'AmbiguousTee', 'MissingHoles']) expect(unmatchedReason(r)).not.toBe(r);
    expect(unmatchedReason('Strange')).toBe('Strange');
  });
});

describe('sections', () => {
  it('lists every admin area once', () => {
    expect(ADMIN_SECTIONS.map((s) => s.href)).toEqual(['/admin', '/admin/users', '/admin/courses', '/admin/flags', '/admin/settings']);
  });
});
