# E2E (Playwright) — P3-WP09

Browser-level certification of the Phase-3 exit criterion: a committed post in one
browser live-updates a second browser without reload.

## Layout

- `playwright.config.ts` (app root) — runner config; builds with `VITE_E2E_AUTH=1`.
- `fixtures.ts` — reusable two-browser fixture (`memberA`, `memberB`).
- `live-update.spec.ts` — the `@smoke` live-update journey.
- `smoke.spec.ts` — authenticated render health check.
- `test-auth-contract.md` — the Development-only test-auth seam contract.

These specs use `.spec.ts` and live outside `src/`, so they are excluded from Vitest
(`*.test.ts(x)`), from `tsc -b` (project includes only `src`), and from the page budget.

## Prerequisites

The journey needs the **full stack** with the backend E2E half in place (routed to
LL Backend Dev — see `test-auth-contract.md`):

1. Postgres + Host running with `ASPNETCORE_ENVIRONMENT=Development`,
   `Authentication:E2E:Enabled=true`, and DevSeed enabled with both demo members.
2. The app reaches the Host via the Vite preview proxy (`/api`, `/hubs`).

Until the backend E2E scheme and the second seeded member exist, the journey will not
authenticate — this is the expected WP08b-style split.

## Run

```powershell
# one-time browser download
npx playwright install --with-deps chromium

# with the stack already up (compose), from app/
npm run e2e
```

`npm run e2e` builds the app with the seam enabled, serves it via `vite preview`, and
runs the specs. In CI the `e2e-smoke` job (main-blocking) provisions the stack first.
