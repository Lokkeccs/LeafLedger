import { defineConfig, devices } from '@playwright/test'

// P3-WP09 — browser-level certification of the Phase-3 exit criterion:
// a committed post in one browser live-updates a second browser without reload.
//
// The web server builds the app with VITE_E2E_AUTH=1 so the Development-only
// test-auth seam is active (see app/e2e/test-auth-contract.md). The full stack
// (Postgres + Host with the E2E scheme + DevSeed two members) must be running
// and reachable through the Vite preview proxy on /api and /hubs.

const port = 4173
const baseURL = `http://localhost:${port}`
const demoSpaceId = process.env.VITE_DEMO_SPACE_ID ?? '8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8f8a1'

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.ts',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL,
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    // VITE_E2E_AUTH must be present at build time — Vite inlines import.meta.env.
    command: 'npm run build && npm run preview',
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    env: {
      VITE_E2E_AUTH: '1',
      VITE_DEMO_SPACE_ID: demoSpaceId,
    },
  },
})
