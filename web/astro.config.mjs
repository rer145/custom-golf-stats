// @ts-check
import { defineConfig } from 'astro/config';
import node from '@astrojs/node';

/**
 * Host names the server answers as (build time). Astro only trusts a request's Host / X-Forwarded-Host when it is
 * listed here; otherwise it treats the request as http://localhost, and the same-origin check on form posts fails.
 * Set ALLOWED_HOSTS (comma-separated, e.g. `stats.example.com`) when building for a deployed site.
 */
const allowedDomains = ['localhost', '127.0.0.1', ...(process.env.ALLOWED_HOSTS ?? '').split(',')]
  .map((h) => h.trim())
  .filter(Boolean)
  .map((hostname) => ({ hostname }));

// Every page is rendered on the server: pages call the API from the server only, and the browser never sees a token.
export default defineConfig({
  output: 'server',
  adapter: node({ mode: 'standalone' }),
  security: {
    // Cross-site form posts are refused (the session cookie is SameSite=Lax as well).
    checkOrigin: true,
    allowedDomains,
  },
  prefetch: false,
  devToolbar: { enabled: false },
});
