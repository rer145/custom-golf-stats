import { describe, expect, it } from 'vitest';
import { MAX_BODY_BYTES, declaresOversizedBody } from './limits';

const post = (length?: string) => new Request('http://x.test/sign-in', { method: 'POST', headers: length === undefined ? {} : { 'content-length': length } });

describe('declaresOversizedBody', () => {
  it('allows bodies up to the limit and requests without a length', () => {
    expect(MAX_BODY_BYTES).toBe(1024 * 1024);
    expect(declaresOversizedBody(post(String(MAX_BODY_BYTES)))).toBe(false);
    expect(declaresOversizedBody(post('12'))).toBe(false);
    expect(declaresOversizedBody(post())).toBe(false);
  });

  it('flags larger or malformed lengths', () => {
    expect(declaresOversizedBody(post(String(MAX_BODY_BYTES + 1)))).toBe(true);
    expect(declaresOversizedBody(post('lots'))).toBe(true);
  });
});
