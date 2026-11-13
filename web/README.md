# Custom Golf Stats web site

Astro 7, server-rendered with the Node adapter (standalone). Pages call the API from the server only; the browser
never sees an API token.

## Configuration

| Variable | When | Required | What it is |
|---|---|---|---|
| `API_BASE_URL` | runtime | yes | Base URL of the Custom Golf Stats API, e.g. `http://localhost:5180`. Called from the server only. |
| `SESSION_SECRET` | runtime | yes | At least 32 characters of random text. Encrypts the session cookie (AES-256-GCM). Changing it signs everyone out. Never commit it. |
| `ALLOWED_HOSTS` | **build time** | for a deployed site | Comma-separated host names the site is served as, e.g. `stats.example.com`. |
| `HOST`, `PORT` | runtime | no | Where the standalone server listens (Node adapter). |

The server refuses to start handling requests if `API_BASE_URL` or `SESSION_SECRET` is missing or the secret is too short.

### ALLOWED_HOSTS

Astro only trusts a request's `Host` (or `X-Forwarded-Host` from a proxy) when the host name is listed in
`security.allowedDomains`, which `astro.config.mjs` builds from `localhost`, `127.0.0.1` and `ALLOWED_HOSTS`. Any other
host is ignored and the request is treated as `http://localhost`, so a forged `Host` header can't steer URLs.

Because the list is baked in when you run `npm run build`, set `ALLOWED_HOSTS` **at build time** (for example as a
Docker build argument). If the deployed host name is missing, every sign-in and other form post is refused with
`403 Cross-site POST form submissions are forbidden`, since the browser's `Origin` no longer matches.

Request bodies are capped at 1 MiB (`src/lib/limits.ts`); larger posts get `413`.

## Scripts

```sh
npm ci
npm run gen:api   # regenerate src/api/schema.d.ts from the API's committed OpenAPI document
npm run check     # astro check + tsc
npm test          # Vitest unit tests
npm run e2e       # Playwright against the production build and a fake API (tests/fake-api.ts); needs `npx playwright install chromium` once
npm run dev       # dev server; set API_BASE_URL and SESSION_SECRET first
```

Run against a local API:

```sh
API_BASE_URL=http://localhost:5180 SESSION_SECRET="$(openssl rand -base64 48)" npm run dev
```

Use `localhost` (not a LAN address) for local development: the session cookie is `Secure`, which browsers allow over
plain HTTP only on localhost.
