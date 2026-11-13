import { describe, expect, it } from 'vitest';
import { serverConfig } from './config';

const secret = 'x'.repeat(32);

describe('serverConfig', () => {
  it('reads API_BASE_URL and SESSION_SECRET', () => {
    expect(serverConfig({ API_BASE_URL: ' http://api.test ', SESSION_SECRET: secret })).toEqual({ apiBaseUrl: 'http://api.test', sessionSecret: secret });
  });

  it('refuses to start without them, or with a weak secret', () => {
    expect(() => serverConfig({ SESSION_SECRET: secret })).toThrow(/API_BASE_URL/);
    expect(() => serverConfig({ API_BASE_URL: 'api.test', SESSION_SECRET: secret })).toThrow(/http/);
    expect(() => serverConfig({ API_BASE_URL: 'http://api.test' })).toThrow(/SESSION_SECRET/);
    expect(() => serverConfig({ API_BASE_URL: 'http://api.test', SESSION_SECRET: 'short' })).toThrow(/SESSION_SECRET/);
  });
});
