import { MIN_SECRET_LENGTH } from './session';

export interface ServerConfig {
  /** Base URL of the Custom Golf Stats API, for server-side calls only. */
  apiBaseUrl: string;
  /** Key material for the session cookie (at least 32 characters). */
  sessionSecret: string;
}

/** Reads `API_BASE_URL` and `SESSION_SECRET` from the environment at request time. Neither is ever committed. */
export function serverConfig(env: Record<string, string | undefined> = process.env): ServerConfig {
  const apiBaseUrl = env.API_BASE_URL?.trim();
  const sessionSecret = env.SESSION_SECRET;
  if (!apiBaseUrl) throw new Error('API_BASE_URL is not set.');
  if (!/^https?:\/\//.test(apiBaseUrl)) throw new Error('API_BASE_URL must be an http(s) URL.');
  if (!sessionSecret || sessionSecret.length < MIN_SECRET_LENGTH) {
    throw new Error(`SESSION_SECRET must be set to at least ${MIN_SECRET_LENGTH} characters.`);
  }
  return { apiBaseUrl, sessionSecret };
}
