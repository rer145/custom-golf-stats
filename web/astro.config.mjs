// @ts-check
import { defineConfig } from 'astro/config';
import node from '@astrojs/node';
import { MAX_BODY_BYTES } from './src/lib/limits.ts';

/**
 * Host names the server answers as (build time). Astro only trusts a request's Host / X-Forwarded-Host when it is
 * listed here; any other Host is ignored and the request is treated as http://localhost, so a forged Host can't
 * steer redirects or URLs, and the same-origin check on form posts fails for it.
 * Set ALLOWED_HOSTS (comma-separated, e.g. `stats.example.com`) when building for a deployed site.
 */
const allowedDomains = ['localhost', '127.0.0.1', ...(process.env.ALLOWED_HOSTS ?? '').split(',')]
  .map((h) => h.trim())
  .filter(Boolean)
  .map((hostname) => ({ hostname }));

// Every page is rendered on the server: pages call the API from the server only, and the browser never sees a token.
export default defineConfig({
  output: 'server',
  adapter: node({
    mode: 'standalone',
    // Requests carry small HTML form posts only; refuse bodies over 1 MiB (the adapter's default is 1 GiB).
    bodySizeLimit: MAX_BODY_BYTES,
  }),
  // Our session is the encrypted cookie (src/lib/session.ts); Astro's own session store is not used.
  session: false,
  security: {
    // Cross-site form posts are refused (the session cookie is SameSite=Lax as well).
    checkOrigin: true,
    allowedDomains,
  },
  // Keep Astro 5's whitespace collapsing (Astro 7 defaults to 'jsx', which drops spaces between inline elements).
  compressHTML: true,
  prefetch: false,
  devToolbar: { enabled: false },
});
