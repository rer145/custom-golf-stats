import { describe, expect, it } from 'vitest';
import { localProblem, pageStatus } from './problems';

describe('localProblem', () => {
  it('builds a 400 whose field errors show next to the fields', () => {
    const p = localProblem({ rating: ['Enter a number.'] });
    expect(p?.status).toBe(400);
    expect(p?.fieldErrors).toEqual({ rating: ['Enter a number.'] });
  });
});

describe('localProblem null', () => {
  it('is null without errors', () => {
    expect(localProblem({})).toBeNull();
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
