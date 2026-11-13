import { describe, expect, it } from 'vitest';
import type { ApiProblem } from '../../api/client';
import { fields, formProblem, isTeeField } from './problems';

const p = (fieldErrors: Record<string, string[]>, message = 'Check the highlighted fields.'): ApiProblem => ({ status: 400, message, fieldErrors });

describe('formProblem', () => {
  it('passes through problems without field errors, and ones whose errors all have a field', () => {
    expect(formProblem(null, fields('note'))).toBeNull();
    const conflict: ApiProblem = { status: 409, message: 'A course with this id already exists.', fieldErrors: {} };
    expect(formProblem(conflict, fields('note'))).toBe(conflict);
    const matched = p({ Note: ['A note is required.'] });
    expect(formProblem(matched, fields('note'))).toBe(matched);
  });

  it('adds errors with no field on the form to the message', () => {
    expect(formProblem(p({ teeId: ['Bad tee.'] }), fields('note'))!.message).toBe('Bad tee.');
    expect(formProblem(p({ note: ['Required.'], near: ['Bad near.'] }), fields('note'))!.message).toBe('Check the highlighted fields. Bad near.');
  });

  it('knows the tee form keys', () => {
    expect(isTeeField('tees[0].rating', 'tees[0].')).toBe(true);
    expect(isTeeField('tees[0].holes[17].strokeIndex', 'tees[0].')).toBe(true);
    expect(isTeeField('tees[0].holes[2].number', 'tees[0].')).toBe(false);
    expect(isTeeField('tees[1].rating', 'tees[0].')).toBe(false);
    expect(isTeeField('Holes[3].Par', '')).toBe(true);
    expect(isTeeField('$.rating', '')).toBe(false);
  });
});
