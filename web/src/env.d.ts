/// <reference types="astro/client" />

declare namespace App {
  interface Locals {
    /** The signed-in session (server only; never render its tokens). `null` on public pages when signed out. */
    session: import('./lib/session').Session | null;
    /** API client carrying the session's bearer token (or none when signed out). Server only. */
    api: import('./api/client').Api;
  }
}

/** Server-side configuration, read at runtime from process.env (see src/lib/config.ts and README.md). */
declare namespace NodeJS {
  interface ProcessEnv {
    API_BASE_URL?: string;
    SESSION_SECRET?: string;
    /** Build time only (astro.config.mjs). */
    ALLOWED_HOSTS?: string;
  }
}
