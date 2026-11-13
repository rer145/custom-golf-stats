import { describe, expect, it } from 'vitest';
import { buildDetails, buildTee, emptyTee, postedTeeCount, readTee, remapHoleErrors, teeFromVersion, versionRequest } from './course-form';

function formOf(entries: Record<string, string>): FormData {
  const form = new FormData();
  for (const [k, v] of Object.entries(entries)) form.set(k, v);
  return form;
}

describe('course form', () => {
  it('reads a tee with its prefix and drops blank hole rows', () => {
    const form = formOf({
      'tees[0].id': 'aaaaaaaa-0000-4000-8000-000000000001',
      'tees[0].name': ' Blue ',
      'tees[0].gender': 'Male',
      'tees[0].rating': '71.4',
      'tees[0].slope': '128',
      'tees[0].holes[0].par': '4',
      'tees[0].holes[0].yardage': '402',
      'tees[0].holes[0].strokeIndex': '7',
      'tees[0].holes[2].par': '3',
      'tees[0].holes[2].yardage': '165',
      'tees[0].holes[2].strokeIndex': '17',
    });
    const built = buildTee(readTee(form, 'tees[0].'), 'tees[0].', { requireName: true });
    expect(built.errors).toEqual({});
    expect(built.request).toMatchObject({
      id: 'aaaaaaaa-0000-4000-8000-000000000001',
      name: 'Blue',
      gender: 'Male',
      effectiveFrom: null,
      rating: 71.4,
      slope: 128,
      frontRating: null,
      holes: [
        { number: 1, par: 4, yardage: 402, strokeIndex: 7 },
        { number: 3, par: 3, yardage: 165, strokeIndex: 17 },
      ],
    });
    expect(built.holeRows).toEqual([0, 2]);
  });

  it('keys local errors by form field', () => {
    const tee = emptyTee('x');
    tee.rating = 'seventy';
    tee.slope = '12.5';
    tee.holes[4] = { par: '4', yardage: '', strokeIndex: '3' };
    const built = buildTee(tee, 'tees[1].', { requireName: true });
    expect(built.errors).toEqual({
      'tees[1].name': ['A tee name is required.'],
      'tees[1].rating': ['Enter a number.'],
      'tees[1].slope': ['Enter a whole number.'],
      'tees[1].holes[4].yardage': ['Yardage is required.'],
    });
  });

  it('asks for at least one hole', () => {
    const tee = { ...emptyTee('x'), name: 'Red', rating: '70', slope: '120' };
    expect(buildTee(tee, '').errors).toEqual({ holes: ['Enter par, yardage and stroke index for at least one hole.'] });
  });

  it('requires an effective date for a new version', () => {
    const tee = { ...emptyTee('x'), rating: '70', slope: '120', effectiveFrom: '' };
    tee.holes[0] = { par: '4', yardage: '300', strokeIndex: '1' };
    expect(buildTee(tee, '', { requireEffectiveFrom: true }).errors).toEqual({ effectiveFrom: ['The date the change took effect is required.'] });
    tee.effectiveFrom = '2026-04-01';
    const built = buildTee(tee, '', { requireEffectiveFrom: true });
    expect(versionRequest(built.request)).toEqual({
      effectiveFrom: '2026-04-01',
      rating: 70,
      slope: 120,
      frontRating: null,
      frontSlope: null,
      backRating: null,
      backSlope: null,
      holes: [{ number: 1, par: 4, yardage: 300, strokeIndex: 1 }],
    });
  });

  it('maps API hole indexes back to form rows', () => {
    const errors = remapHoleErrors(
      { 'tees[0].holes[1].par': ['Par must be between 3 and 6.'], 'tees[0].name': ['x'], 'tees[1].holes[0].par': ['y'] },
      'tees[0].',
      [0, 2],
    );
    expect(errors).toEqual({ 'tees[0].holes[2].par': ['Par must be between 3 and 6.'], 'tees[0].name': ['x'], 'tees[1].holes[0].par': ['y'] });
    expect(remapHoleErrors({ 'holes[0].strokeIndex': ['z'] }, '', [8])).toEqual({ 'holes[8].strokeIndex': ['z'] });
  });

  it('copies par and stroke index into a new tee, not yardage', () => {
    const first = emptyTee('a');
    first.holes[0] = { par: '5', yardage: '520', strokeIndex: '2' };
    expect(emptyTee('b', first).holes[0]).toEqual({ par: '5', yardage: '', strokeIndex: '2' });
  });

  it('prefills a new version from the current one, without its date', () => {
    const values = teeFromVersion(
      { id: 't', name: 'White', gender: 'Female', versions: [] },
      {
        id: 'v',
        effectiveFrom: '1900-01-01',
        isCurrent: true,
        par: 36,
        rating: 35.2,
        slope: 120,
        frontPar: null,
        frontRating: null,
        frontSlope: null,
        backPar: null,
        backRating: null,
        backSlope: null,
        source: 'Manual',
        changedAt: '2026-01-01T00:00:00Z',
        holes: [{ number: 9, par: 4, yardage: 380, strokeIndex: 1 }],
      },
    );
    expect(values.effectiveFrom).toBe('');
    expect(values.rating).toBe('35.2');
    expect(values.holes[8]).toEqual({ par: '4', yardage: '380', strokeIndex: '1' });
    expect(values.holes[0]).toEqual({ par: '', yardage: '', strokeIndex: '' });
  });

  it('course details: blank text is null, coordinates are numbers', () => {
    const { request, errors } = buildDetails({
      name: ' Cedar Ridge ',
      clubName: '',
      address: '',
      city: 'Lakeview',
      region: '',
      country: '',
      phone: '',
      website: '',
      latitude: '40.5',
      longitude: 'east',
    });
    expect(errors).toEqual({ longitude: ['Enter a number.'] });
    expect(request).toMatchObject({ name: 'Cedar Ridge', clubName: null, city: 'Lakeview', latitude: 40.5, longitude: null });
    expect(buildDetails({ ...buildDetailsBlank(), name: ' ' }).errors).toEqual({ name: ['Name is required.'] });
  });

  it('clamps the tee count', () => {
    expect(postedTeeCount(formOf({ teeCount: '3' }))).toBe(3);
    expect(postedTeeCount(formOf({ teeCount: '0' }))).toBe(1);
    expect(postedTeeCount(formOf({ teeCount: '500' }))).toBe(12);
    expect(postedTeeCount(formOf({}))).toBe(1);
  });
});

function buildDetailsBlank() {
  return { name: '', clubName: '', address: '', city: '', region: '', country: '', phone: '', website: '', latitude: '', longitude: '' };
}
