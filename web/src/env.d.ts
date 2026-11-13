/// <reference types="astro/client" />

declare namespace App {
  interface Locals {
    /** The signed-in session (server only; never render its tokens). `null` on public pages when signed out. */
    session: import('./lib/session').Session | null;
    /** API client carrying the session's bearer token (or none when signed out). Server only. */
    api: import('./api/client').Api;
  }
}
