import { defineConfig, devices } from '@playwright/test';

const WEB_PORT = Number(process.env.E2E_WEB_PORT ?? 4321);
const FAKE_API_PORT = Number(process.env.FAKE_API_PORT ?? 4010);
const FAKE_API_URL = `http://127.0.0.1:${FAKE_API_PORT}`;

/**
 * End-to-end tests run the production build (`astro build` + the Node standalone server) against the fake API
 * (tests/fake-api.ts), so they need no .NET or database. SESSION_SECRET here is a test-only value.
 */
export default defineConfig({
  testDir: 'tests/e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://127.0.0.1:${WEB_PORT}`,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'npx tsx tests/fake-api.ts',
      url: `${FAKE_API_URL}/__fake/health`,
      env: { FAKE_API_PORT: String(FAKE_API_PORT) },
      reuseExistingServer: !process.env.CI,
      stdout: 'ignore',
    },
    {
      command: 'npm run build && node dist/server/entry.mjs',
      url: `http://127.0.0.1:${WEB_PORT}/`,
      env: {
        HOST: '127.0.0.1',
        PORT: String(WEB_PORT),
        API_BASE_URL: FAKE_API_URL,
        SESSION_SECRET: 'e2e-only-session-secret-not-for-production-0123456789',
      },
      timeout: 180_000,
      reuseExistingServer: !process.env.CI,
      stdout: 'ignore',
    },
  ],
});
