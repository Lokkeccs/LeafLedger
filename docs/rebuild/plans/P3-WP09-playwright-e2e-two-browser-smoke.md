# P3-WP09 — Playwright E2E harness + browser-level two-browser live-update smoke

- **Phase:** 3 (frontend re-platform) — the **browser-level certification of the Phase-3 exit criterion**. WP06 built "post a balanced entry", WP07 built "…see it in the trial-balance report", and WP08 delivered the *live* half (a data-free SignalR invalidation ping so a second client refetches) — **proven deterministically at the integration + component level**. WP09 delivers the **greenfield Playwright harness** and the **§05 main-blocking two-browser smoke journey** that certifies the same exit criterion *in real browsers*: browser A posts → browser B's trial-balance page live-updates via the SignalR ping, with no manual reload.
- **State:** **verify — QA PASS (2026-07-16 re-review).** The live smoke and backend suite pass, AC3 now requires a report request that starts after browser A's successful post, the shell test is environment-independent, and WP08 dependency files are explicitly owned by the WP08 plan.
- **Owner (implementation):** LL Frontend Dev (harness + journey + the frontend E2E-auth seam). The small backend Development-only test-auth handler + `DevSeed` second-member extension are backend edits the frontend WP will need routed to LL Backend Dev, or done under explicit user authorization within the same session (flagged in the file list).
- **Depends on:**
  - **P3-WP08** (done) — the SignalR hub (`/hubs/space`), the membership-gated group join, the post-commit coalesced ping, and the client `useSpaceInvalidation` hook + N6 invalidation map. WP09 exercises exactly this path end-to-end through two real browsers. The Vite dev/preview `/hubs` WebSocket proxy that WP08b added is the local transport this journey rides.
  - **P3-WP07** (done) — `useTrialBalance` / `TrialBalancePage` on `qk.reports.trialBalance(spaceId)` is the visible surface that must live-update in browser B.
  - **P3-WP06** (done) — the journal-entry posting flow browser A drives to produce the committed post that emits the ping.
  - **P3-WP05** (done) — the accounts read path + the Development-only `DevSeed` (demo space, three CHF accounts, an open period, and Owner membership) that makes a real balanced post work locally without any manual data setup.
  - **P3-WP02** (done) — MSAL is the production auth path; WP09 must *bypass* it deterministically for CI (no interactive Microsoft sign-in in a headless runner) — see D-P3-E2E-AUTH.
  - **P1-WP02** (done) — `docker-compose` (Postgres + API); WP09 brings this stack up (Seed enabled, self-hosted hub) as the journey's backend.
- **Blocks:** **Phase-3 close-out at the browser level** (the functional exit is already met by WP08; WP09 is the formal §05 main-blocking smoke certification). Also unblocks a future "~15 E2E journeys" WP (the harness this WP stands up is the foundation).
- **Estimated size:** ≤ 2 days. Greenfield Playwright toolchain + one two-browser journey + a Development-gated auth seam + one CI job. Deliberately scoped to **the single exit journey** (+ a trivial render smoke); the broader ~15-journey suite (§1.1) is explicitly a **future WP**, not here.

## Context / scope note (LL Architect)

Part 4 §Phase 3 exit criterion: *"post a balanced entry → see it in the trial-balance report; two browsers in one space live-update via a SignalR invalidation ping."* WP06+WP07+WP08 satisfied this **functionally and deterministically** (a backend two-`HubConnection` fan-out integration test + a client cache-correctness test). What is **not yet** proven is the same journey **in real browsers over the real transport** — the §1.1 testing-pyramid row *"E2E (Playwright) … ~15 journeys incl. two-browser live-update via SignalR ping — main-blocking (smoke)"*. WP08 explicitly **carved this out** (decision **D-P3-SIGNALR-EXIT**) because Playwright is **greenfield in this repo** (`app/package.json` has no Playwright; no `playwright.config.*`, no `e2e/` folder) and standing up the toolchain + the journey is its own ≤2-day unit.

This WP is a **test-infrastructure deliverable, not a feature.** It ports **no** accounting behavior, computes **no** financial value, and adds **no** REST endpoint, OpenAPI/TS, or migration change. The posted values it drives are already pinned server-side (P2-WP07 integration + P2-WP08 property suite); WP09 asserts the *browser-observable live-update*, not the *numbers*. Hence **no golden fixtures and no accounting consult** (see those sections).

**The one hard problem is authentication.** The production app signs in via MSAL popup against Entra `common`; a headless CI runner cannot complete an interactive Microsoft login, and the Host validates real Entra-signed JWTs (JWKS + audience + issuer allowlist, P2-WP11). To run a *deterministic* two-browser journey, both browsers must be "signed in" as seeded members of the demo space **without** live Entra, and the API must accept their tokens. The repo already has the shape of the answer: the integration tier uses a `TestAuthHandler` (`backend/tests/LeafLedger.IntegrationTests/Authorization/TestAuthHandler.cs`) that mints a principal for a seeded subject/tenant, resolved to an internal `user_id` via `resolve_identity_link` exactly like production. WP09's **D-P3-E2E-AUTH** exposes an equivalent path at the real Host **strictly gated to Development + an explicit `Authentication:E2E:Enabled` flag, fail-closed everywhere else**, plus a frontend `VITE_E2E_AUTH` seam that swaps the MSAL token acquisition for a static test bearer. This is security-sensitive, so it is the load-bearing decision requiring sign-off.

## Spec sources

- `docs/architecture/rebuild/05-quality-and-maintainability.md` §1.1 (the pyramid: *"E2E (Playwright) — ~15 journeys incl. two-browser live-update via SignalR ping — main-blocking (smoke) + nightly (full)"*), §1.2 (*"Main pipeline: + integration + E2E smoke → … deploy staging slot → `/health` + `/integrity` probe → swap"* — E2E smoke is **main-blocking, not PR-blocking**; *"Weekly: full E2E, load test"*), §2.5 (correlation id / staleness surfacing — a follow-up, not required here).
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 3 (*"Exit: post → see it in trial balance report, two browsers live-update via SignalR ping"*).
- `docs/rebuild/plans/P3-WP08-signalr-live-invalidation.md` — the **D-P3-SIGNALR-EXIT** carve-out ("prove deterministically in WP08 [integration+component]; browser Playwright smoke → P3-WP09") and the local `/hubs` proxy this journey rides.
- `.github/instructions/frontend.instructions.md` — layering **features → application → api**; page budgets (E2E specs live outside `src/**`, so they do not affect the page budget); the generated client stays the only REST access point; reuse shared primitives.
- `docs/architecture/rebuild/07-vibe-coding-playbook.md` — smallest viable diff; tests define done.

## Goal

A greenfield Playwright harness in `app/` plus a single **main-blocking smoke journey** that, in **two isolated real browser contexts** authenticated as members of one demo space:

1. Browser B opens the trial-balance report page and records the initial totals.
2. Browser A opens the journal-entry page and posts a **balanced** entry (e.g. debit *Office expenses* / credit *Bank* by an equal minor-unit amount) through the WP06 flow.
3. **Without any manual reload in browser B**, browser B's trial-balance page refetches (driven by the WP08 SignalR ping → TanStack Query invalidation) and its totals update to reflect the post within a couple of seconds.

The journey runs against the real stack (`docker compose up` Postgres + API + self-hosted hub; the SPA served by `vite preview` proxying `/api` and `/hubs` to the API), is fully deterministic (seeded data, no live Entra via the D-P3-E2E-AUTH seam), and is wired as a **main-blocking `e2e-smoke` job** (not PR-blocking) that gates deploy.

## Scope

1. **Playwright toolchain (greenfield):** `@playwright/test` as an `app/` devDependency; `app/playwright.config.ts` (projects/browsers, a `webServer` that runs `vite preview`, base URL, retries, trace-on-first-retry); an `e2e` npm script; browser install in CI. Vitest and Playwright stay separate (Vitest globs `src/**/*.test.*`; Playwright globs `e2e/**/*.spec.ts`) so `npm test` is unaffected.
2. **The two-browser live-update smoke journey** — `app/e2e/live-update.spec.ts`: two `browser.newContext()` contexts (isolated storage = "two browsers"), each authenticated via the E2E-auth seam as a member of the demo space; the post-in-A → live-update-in-B assertion above; a bounded `expect.poll`/`toPass` wait (no arbitrary sleeps) with a hard timeout; asserts B updated with **no** `page.reload()`.
3. **A trivial render/health smoke** — `app/e2e/smoke.spec.ts`: the app shell renders and the trial-balance route loads against the live stack (proves the harness + stack wiring independently of SignalR).
4. **Frontend E2E-auth seam (D-P3-E2E-AUTH):** a single `app/src/application/auth/e2eAuth.ts` seam, active **only** when `VITE_E2E_AUTH` is set, that presents a synthetic signed-in account (so `useAuth().isSignedIn` is true and the shell renders signed-in) and makes `acquireApiToken` return a static test bearer. When the flag is unset (all normal builds), the MSAL path is byte-for-byte unchanged.
5. **Backend Development-only test-auth handler (D-P3-E2E-AUTH):** a Host authentication scheme (mirroring the integration `TestAuthHandler`) that accepts the static test bearer and maps it to the seeded subject/tenant → `resolve_identity_link` → internal `user_id`, **registered only when `Environment.IsDevelopment()` AND `Authentication:E2E:Enabled == true`**; fail-closed (absent the flag, the real JwtBearer scheme is the only authenticator, exactly as today). Two seeded members' bearers are distinguishable so the two browsers authenticate as two members.
6. **Seed extension (D-P3-E2E-USERS):** extend `DevSeed` to also provision a **second** member of the demo space (idempotent, Development-only), so the journey uses two distinct members rather than one account in two tabs.
7. **Local/CI orchestration (D-P3-E2E-STACK):** a `docker-compose` path (or a thin `docker-compose.e2e.yml` override) that starts Postgres + API with `Seed:Enabled=true` and `Authentication:E2E:Enabled=true`; Playwright's `webServer` builds and previews the SPA with `VITE_E2E_AUTH=1`. An `e2e-smoke` job in `.github/workflows/main.yml` brings the stack up, runs the tagged smoke, and is added to the `deploy-*` `needs`.
8. **Docs:** `app/e2e/README.md` — how to run the harness locally, the auth-seam gating, and the exit-criterion mapping.

### Hardening (added 2026-07-16 on user request — approved)

Four low-risk additions that harden maintainability and debuggability without expanding functional scope:

9. **Test-auth seam contract doc — `app/e2e/test-auth-contract.md` (5–10 lines).** A short, authoritative description of the E2E test token: its shape (the static bearer string presented by the `VITE_E2E_AUTH` seam), the claims the Development-only Host handler maps it to (`subject`/`oid` + `tid` → `resolve_identity_link` → internal `user_id`; role), which two seeded members it distinguishes, and the double gate (`Development` **and** `Authentication:E2E:Enabled`). Purely descriptive; it pins the contract both ends implement so a future maintainer does not have to reverse-engineer it. Referenced from `app/e2e/README.md` and the Host handler's summary.
10. **Reusable two-browser Playwright fixture — in `app/e2e/fixtures.ts`.** Expose the two-authenticated-context setup as a first-class Playwright test fixture (e.g. `test.extend` yielding `memberA`/`memberB` pages, each an isolated context authenticated as a distinct seeded member) so the live-update journey — and every future multi-browser journey — consumes one shared, reviewed helper rather than re-wiring contexts per spec.
11. **CI single-retry policy for the smoke.** `app/playwright.config.ts` sets `retries: process.env.CI ? 1 : 0` — exactly one retry in CI (harmless for genuinely transient transport/browser flakiness), zero locally so real failures surface immediately. A retry masks flakiness, never a correctness bug (the assertion is deterministic against seeded state).
12. **Screenshot + trace on failure.** `app/playwright.config.ts` uses `screenshot: 'only-on-failure'`, `video: 'retain-on-failure'`, and `trace: 'on-first-retry'`, and the CI `e2e-smoke` job uploads the Playwright report/artifacts on failure. If the live-update never triggers, the failure carries a screenshot of browser B's stale trial-balance + a trace of the network/socket activity for immediate diagnosis.

## Non-goals (explicitly deferred)

- **No full ~15-journey E2E suite.** This WP delivers **one** exit-criterion journey (+ a render smoke). The remaining journeys (accounts CRUD, imports, VAT, master data, admin, BS/P&L/dashboard) are a **future WP** aligned with the Phase-4 feature surfaces, and most of those features do not exist yet.
- **No nightly/weekly full-E2E scheduling or load tests.** §1.2 assigns those to weekly; wiring the nightly cron + the load-test harness is a later ops WP. WP09 wires only the **main-blocking smoke**.
- **No live Entra E2E.** No real Microsoft-account sign-in, no ROPC/service-principal token minting, no Entra test-tenant secrets in CI. Determinism comes from the Development-gated D-P3-E2E-AUTH seam, not real credentials.
- **No production auth change.** The E2E-auth path is Development-only and fail-closed; the production MSAL + JwtBearer path is untouched when the flags are absent. A test asserts the seam is inert by default.
- **No new REST endpoint, OpenAPI/TS, or migration change.** The harness drives the existing UI/endpoints; `app/src/api/**` and `backend/openapi/**` stay byte-unchanged (a regen must produce no diff). The `DevSeed` extension is raw SQL against existing tables (no EF entity → `HasPendingModelChanges()` stays green).
- **No frontend containerization.** The SPA is served by Playwright's `webServer` (`vite preview`) proxying to the compose API — no new Dockerfile for the app.
- **No Azure SignalR in the journey.** The smoke runs on the **self-hosted** hub (no `AzureSignalR` connection string), consistent with D-P3-SIGNALR-TRANSPORT's local-dev route.
- **No correlation-id-through-the-ping assertion.** The Part 5 §2.5 trace-id follow-up remains a carry-forward on WP08, not a WP09 gate.

## Decisions (front-loaded, non-accounting — all APPROVED by the user 2026-07-16 on their recommended routes)

Six decisions, **none accounting** (test infrastructure; no value is computed or crosses any boundary). **All six were approved on their recommended routes with no overrides**; each is now an implementation constraint. **D-P3-E2E-AUTH is security-sensitive and load-bearing** — approved explicitly.

- **D-P3-E2E-AUTH — how the two browsers authenticate deterministically (LOAD-BEARING, security-sensitive). RECOMMEND: a Development-only, flag-gated test-auth seam on both ends.** Frontend: a `VITE_E2E_AUTH` build/env flag swaps MSAL token acquisition for a static test bearer and a synthetic signed-in account. Backend: a Host authentication scheme (mirroring the integration `TestAuthHandler`) that accepts that bearer and resolves it through the **real** `resolve_identity_link` → membership → RLS actor path, **registered only when `Environment.IsDevelopment()` AND `Authentication:E2E:Enabled == true`**, fail-closed otherwise. This reuses the proven integration-test identity shape, keeps the journey off live Entra, and leaves the production path byte-unchanged. A backend test asserts the E2E scheme is **absent** outside Development / without the flag.
  - *Alternative A:* mint a real RSA-signed JWT the JwtBearer scheme trusts via a locally configured signing key + issuer, injected into MSAL `sessionStorage` by Playwright → deep MSAL-cache coupling and brittle; also adds a prod-trusted local key. Rejected.
  - *Alternative B:* real Entra test users via ROPC / client credentials → requires Entra test-tenant secrets in CI and does not work for personal Microsoft accounts (the D-WP13-SUBJECT reality). Rejected for M-scale.
  - *Security note:* the seam is the only production-adjacent surface in this WP. It must be **doubly gated** (Development environment **and** explicit config flag), must never register outside Development, and must not weaken the real JwtBearer scheme when inactive. This is why it needs explicit sign-off.
- **D-P3-E2E-RUNNER — the E2E runner. RECOMMEND: `@playwright/test`, config + specs in `app/` (`app/playwright.config.ts`, `app/e2e/**`), a new `e2e` npm script, browsers installed in CI.** Playwright is the spec-named tool (§1.1) and is first-class for multi-context (two-browser) scenarios and WebSocket transports. Kept separate from Vitest so `npm test` and the page budget are unaffected.
  - *Alternative:* Cypress → weaker multi-browser-context ergonomics and not the spec-named tool. Rejected.
- **D-P3-E2E-STACK — how the stack is orchestrated for the journey. RECOMMEND: `docker compose` (Postgres + API, `Seed:Enabled=true`, `Authentication:E2E:Enabled=true`, self-hosted hub) + Playwright `webServer` running `vite preview` on 4173 proxying `/api` + `/hubs` to the API on 8080.** Reuses the merged compose + the WP08b `/hubs` WebSocket proxy; no new container for the SPA.
  - *Alternative:* containerize the frontend + a full compose-only orchestration → heavier, slower feedback, more moving parts for one journey. Rejected.
- **D-P3-E2E-USERS — who the two browsers are. RECOMMEND: two distinct seeded members of the one demo space** (extend `DevSeed` with a second Development-only member). This is faithful to "two browsers in one space" and additionally exercises the per-user membership-gated group join (WP08 D-P3-SIGNALR-AUTHZ) for two identities.
  - *Alternative (acceptable minimal fallback):* the same seeded member in two isolated browser contexts. Still proves two connections in one group receive the coalesced ping, but does not exercise a second identity. Recommended only if the second-member seed proves disproportionate.
- **D-P3-E2E-CI — where the journey runs in CI. RECOMMEND: a `main`-pipeline `e2e-smoke` job (main-blocking), added to the `deploy-*` `needs`, tagged `@smoke`; NOT added to `pr.yml`.** This matches §1.1/§1.2 exactly (E2E smoke is main-blocking; PRs stay fast on lint/type/unit/contract). Docker + real browsers are too heavy/flaky to gate every PR.
  - *Alternative:* add it to the PR pipeline → slow, flaky PRs against the spec's own gate assignment. Rejected.
- **D-P3-E2E-SCOPE — how many journeys. RECOMMEND: exactly the two-browser live-update smoke (+ one trivial render/health smoke) in this WP; the remaining ~14 journeys are a future WP.** Keeps WP09 ≤ 2 days and matches the carve-out intent; the harness this WP stands up is the reusable foundation for the rest.
  - *Alternative:* build several journeys now → exceeds ≤ 2 days and most target features are Phase-4. Rejected.

## Accounting decisions

**None required — no LL Accounting Expert consult.** WP09 is test infrastructure: it drives the existing UI and asserts a browser-observable live-update. It introduces no accounting behavior and moves no financial value; the posted amounts it uses are arbitrary balanced values already pinned server-side (P2-WP07 integration + P2-WP08 property suite). The two standing boundaries are respected, not decided: no money crosses the socket (the ping is data-free), and the report is read-only (the journey triggers a GET refetch). If, during implementation, the journey's assertion drifts toward validating computed balances (rather than the *fact of the live-update*), stop — report-value correctness stays owned by the server-side suites, not this E2E.

## Golden fixtures

**None required.** WP09 computes and ports no accounting value; it certifies a browser-observable behavior. Correctness of the exit journey is pinned by the Playwright assertion (B live-updates after A posts, no reload) over the real stack; the underlying report *values* are already pinned by P2-WP07 integration tests and the P2-WP08 property suite, and the ping fan-out is pinned by the WP08a two-`HubConnection` integration test. Recorded so QA does not expect a golden artifact. This mirrors the WP02/WP08b precedent (a transport/infrastructure WP is pinned by integration + component/E2E tests, not golden fixtures).

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

- **No OLD oracle / greenfield.** The OLD stack had no Playwright E2E harness and no deterministic two-browser test; this is a **new capability**, spec-derived from §1.1 and the Phase-3 exit criterion. Nothing is ported.
- **Reuse (this repo, Phase 2/3):** the merged WP08 hub + `useSpaceInvalidation` + N6 map (the path under test); the WP06 journal-entry flow and WP07 trial-balance page (the two UI surfaces the journey drives); the WP05 `DevSeed` (extended by D-P3-E2E-USERS); the integration `TestAuthHandler` **shape** (mirrored by the Development-only Host handler in D-P3-E2E-AUTH — reused, not moved into production paths); the P1-WP02 `docker-compose` + the WP08b `/hubs` proxy (the local transport).

## Dependencies

- **New frontend devDependency:** `@playwright/test` (the spec-named E2E runner) — `app/package.json` gains one devDependency; CI installs the browser binaries (`npx playwright install --with-deps`). Recorded per the repo dependency rule.
- **No new production runtime dependency** (frontend or backend). The backend Development-only test-auth handler uses ASP.NET Core `AuthenticationHandler` (shared framework) — no new NuGet.
- **No new migration, no OpenAPI/TS regeneration, no REST contract change** — `app/src/api/**` and `backend/openapi/**` stay byte-unchanged (a regen must produce no diff). The `DevSeed` second member is raw SQL against existing tables (no EF entity; `HasPendingModelChanges()` stays green).
- **New config (not a secret in code):** `Authentication:E2E:Enabled` (backend, Development-only), `VITE_E2E_AUTH` (frontend, E2E build only) — both documented in `.env.example` / compose override, never enabled in production config.

## File list (implementation target)

### Frontend (LL Frontend Dev)

**New**
- `app/playwright.config.ts` — projects, `webServer` (`vite preview`, `VITE_E2E_AUTH=1`), base URL, `retries: process.env.CI ? 1 : 0` (single CI retry), `screenshot: 'only-on-failure'`, `video: 'retain-on-failure'`, `trace: 'on-first-retry'`.
- `app/e2e/live-update.spec.ts` — the two-browser live-update smoke journey (`@smoke`), consuming the reusable two-browser fixture.
- `app/e2e/smoke.spec.ts` — the trivial render/health smoke.
- `app/e2e/fixtures.ts` — the reusable **two-browser fixture** (`test.extend` yielding `memberA`/`memberB` isolated authenticated contexts) + helpers to navigate and read trial-balance totals.
- `app/e2e/test-auth-contract.md` — the 5–10-line test-auth token/claims contract (the shape both ends implement).
- `app/e2e/README.md` — run instructions + auth-seam gating + exit-criterion mapping + a link to the contract doc.
- `app/src/application/auth/e2eAuth.ts` — the `VITE_E2E_AUTH`-gated synthetic-account + static-bearer seam.

**Modified**
- `app/package.json` — `@playwright/test` devDependency + `e2e` script.
- `app/src/application/auth/useAuth.ts` and/or `authTokens.ts`/`msalInstance.ts` — the minimal branch that consults `e2eAuth` when `VITE_E2E_AUTH` is set (MSAL path untouched when unset).
- `app/src/app/shell.test.tsx` — auth-state assertion that remains valid with or without developer-local MSAL configuration.
- `app/.env.example` — document `VITE_E2E_AUTH`.

**Tests**
- `app/src/application/auth/e2eAuth.test.ts` — the seam is inert when `VITE_E2E_AUTH` is unset (MSAL path preserved) and produces the synthetic account + static bearer when set. (Vitest unit — keeps the seam pinned without the browser stack.)

### Backend (LL Backend Dev — routed, or under explicit user authorization)

**New**
- `backend/src/LeafLedger.Host/Authorization/E2ETestAuthenticationHandler.cs` — Development-only test-auth scheme mapping the static bearer → seeded subject/tenant → `resolve_identity_link` → internal `user_id`.

**Modified**
- `backend/src/LeafLedger.Host/Program.cs` — register the E2E scheme **only** when `Environment.IsDevelopment()` AND `Authentication:E2E:Enabled == true`; otherwise the current JwtBearer registration is unchanged.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/DevSeed.cs` — provision a second demo-space member (idempotent, Development-only).
- `backend/src/LeafLedger.Host/appsettings.Development.json` — `Authentication:E2E:Enabled` default (false) + the seeded second-member subject/tenant.

**Tests**
- `backend/tests/LeafLedger.IntegrationTests/Authorization/E2EAuthGatingTests.cs` — the E2E scheme is **not** registered outside Development / without the flag (fail-closed); when enabled it authenticates the seeded members and rejects an unknown bearer.

### Orchestration

**New**
- `docker-compose.e2e.yml` (override) — API with `Seed:Enabled=true` + `Authentication:E2E:Enabled=true` (+ seeded second-member env). *(Or equivalent env on the base compose gated for E2E; kept as an override so the default `docker compose up` is unchanged.)*

**Modified**
- `.github/workflows/main.yml` — a `e2e-smoke` job (compose up → `npx playwright install --with-deps` → `npm run e2e -- --grep @smoke`), added to the `deploy-api` / `deploy-frontend` `needs`. **Not** added to `pr.yml`.

**No** `app/src/api/**`, `backend/openapi/**`, or migration change.

## Acceptance criteria (concrete, testable)

## QA verdict

**PASS — 2026-07-16 re-review, LL QA Reviewer**

1. **AC3 — resolved.** `app/e2e/live-update.spec.ts` records report request start times and requires one to start after browser A's successful post response. The live journey still asserts the changed total and no browser-B navigation.
2. **AC7 — resolved by explicit ownership.** The WP08 plan records the refresh-orchestration files as WP08/P2-WP12-owned dependency changes; WP09 remains limited to its harness, auth, and test-support files. The combined worktree remains multi-WP and must be committed with that ownership preserved.
3. **AC8 — resolved.** `src/app/shell.test.tsx` now asserts the correct signed-in or unconfigured shell state based on `hasClientId()`. Full Vitest passes **97/97** in the configured local environment.

**QA evidence:** backend Release **351/351**; Compose config valid; two consecutive full Playwright smoke runs **5/5 + 5/5**; frontend lint/typecheck/page-budget/build/audit pass; frontend Vitest **97/97**.

1. **Harness runs.** `npm run e2e` (with the compose stack up) executes the Playwright suite; `app/playwright.config.ts` + `app/e2e/**` exist; the render/health smoke (`smoke.spec.ts`) passes against the live stack. Vitest (`npm test`) and the page budget are unaffected (E2E specs live outside `src/**`).
2. **Two-browser live-update journey (the exit certification).** In two isolated browser contexts authenticated as members of the demo space: browser B on `/reports/trial-balance` records initial totals; browser A posts a balanced entry via the WP06 flow; **with no `page.reload()` in B**, B's trial-balance totals update to reflect the post within a bounded wait (`expect.poll`/`toPass`, hard timeout, no arbitrary sleep). The test asserts the changed totals and that no reload occurred.
3. **Live path, not manual refetch.** The update in B is driven by the SignalR ping → TanStack Query invalidation (the WP08 path over the `/hubs` proxy), observable as a report GET occurring in B **after** A's post without user action in B; the assertion does not pass if B never refetches.
4. **Determinism.** The journey relies only on `DevSeed` state (demo space, three CHF accounts, open period, two members) and the D-P3-E2E-AUTH seam — **no live Entra**; it passes repeatably (bounded retries permitted for transport flakiness, not for correctness); a second consecutive run is green.
5. **E2E auth is Development-gated + fail-closed (security).** The backend E2E scheme is registered **only** in Development with `Authentication:E2E:Enabled == true`; a backend test asserts it is absent otherwise (production still requires a real Entra JWT), and that when enabled it authenticates the seeded members and rejects an unknown bearer. The frontend `e2eAuth` seam is inert when `VITE_E2E_AUTH` is unset (a Vitest asserts the MSAL path is preserved).
6. **CI wiring (main-blocking, not PR).** `.github/workflows/main.yml` gains an `e2e-smoke` job that brings up the stack, installs browsers, runs the `@smoke` journey, and is listed in the `deploy-api` / `deploy-frontend` `needs`; `pr.yml` is unchanged. The job is green.
7. **No contract / out-of-scope drift.** No REST endpoint, OpenAPI/TS, or migration change (`app/src/api/**` + `backend/openapi/**` byte-unchanged; a regen produces no diff); `HasPendingModelChanges()` stays false; the diff is limited to the file list (harness + the Development-gated auth seam + the `DevSeed` second member + the CI/compose wiring).
8. **All existing gates stay green.** Frontend: `npm run lint`, `npm run typecheck`, `npm test`, `npm run check:page-budget`, `npm run build`, duplicate-key check, `npm audit --omit=dev` (no new prod vulnerabilities from `@playwright/test` as a **dev** dependency). Backend: Release build + full suite (unit + architecture + Testcontainers integration, incl. the new gating test) green.
9. **Hardening — contract doc + reusable fixture.** `app/e2e/test-auth-contract.md` exists and describes the test token shape + claims + double gate; the two-browser fixture in `app/e2e/fixtures.ts` yields two distinct authenticated contexts and is consumed by `live-update.spec.ts` (no per-spec context re-wiring).
10. **Hardening — retry + failure artifacts.** `app/playwright.config.ts` sets a single CI retry (`retries: process.env.CI ? 1 : 0`) and `screenshot: 'only-on-failure'` + `video: 'retain-on-failure'` + `trace: 'on-first-retry'`; the `e2e-smoke` job uploads the Playwright report/artifacts on failure. An inspection/config assertion confirms these settings.

## Boundary note

The Playwright specs live in `app/e2e/**` (outside `src/**`) so they respect the frontend layering by construction (they drive the app through the browser, not by importing feature internals) and do not affect the page budget. The frontend E2E-auth seam lives in `application/auth` (the correct layer for auth glue) and is a pure additive branch gated by `VITE_E2E_AUTH`. On the backend the Development-only handler lives in `Host/Authorization` alongside the existing auth configuration; it reuses the same `resolve_identity_link` → membership → RLS-actor path as production (no separate trust path into the data), and is doubly gated so it can never authenticate outside Development. No money crosses any new boundary; the journey asserts a UI live-update, and the server remains the single source of truth.

## Open questions / carry-forwards

- **Full ~15-journey E2E suite (future WP).** The harness this WP stands up is the foundation; the remaining journeys track the Phase-4 feature surfaces (most do not exist yet) and are a separate WP.
- **Nightly/weekly full E2E + load tests (ops WP).** §1.2 assigns these to weekly; wiring the cron + load harness is a later ops task. WP09 wires only the main-blocking smoke.
- **Correlation-id-through-the-ping (Part 5 §2.5).** Remains a WP08 carry-forward; not a WP09 gate.
- **Azure SignalR in E2E (later).** The smoke runs on the self-hosted hub; a future variant could point at a provisioned Azure SignalR resource once ops wires the connection string (D-P3-SIGNALR-TRANSPORT carry-forward).
- **Multi-space / space-picker journeys (later).** The journey uses `VITE_DEMO_SPACE_ID`; real `GET /spaces` + a picker land in Phase 4 and bring their own E2E coverage.

## Implementation log

- **LL QA Reviewer (remediation, 2026-07-16):** Closed the re-review findings. The live-update spec now requires a trial-balance GET that starts after browser A's successful post; `shell.test.tsx` accepts either configured or unconfigured MSAL state and full Vitest is **97/97**; WP08 refresh-orchestration files are explicitly owned by the WP08 plan. Post-remediation Playwright smoke passes **5/5**, backend Release passes **351/351**, and frontend lint/typecheck/page-budget/build/audit pass. State -> `verify`; QA PASS.

- **LL Backend Dev (QA remediation, 2026-07-16):** Restored P2-WP12's asynchronous report refresh enqueue; changed the browser smoke to cover the documented empty DevSeed state, await bounded SignalR readiness, observe browser B's post-trigger report GET, and assert no navigation; removed duplicate MoneyInput blur propagation that reset the debit line during browser submission. Focused MoneyInput tests **11/11**, full backend Release **351/351**, and two consecutive full Playwright smoke runs **5/5 + 5/5**. State -> `verify`; QA PASS.

- **LL Frontend Dev (MoneyInput remediation, 2026-07-16):** Fixed the shared `MoneyInput` controlled-state gap by synchronizing valid parsed edits to the parent during `onChange`, while retaining the raw editing buffer until blur normalization. Added regressions for pre-blur synchronization and sibling-rerender preservation. Focused shared tests **11/11**, typecheck, lint, and diagnostics pass. The fresh E2E stack was rebuilt and cleaned up, but its new database had no seeded posted balances, so the browser journey could not establish its expected table baseline; WP09 remains **verify** pending a valid seeded-stack browser run.

- **LL Backend Dev (validation follow-up, 2026-07-16):** The initial live-update failure had two independent causes: SignalR E2E bearer parsing required the `access_token` query parameter, and the report invalidation could race the materialized-view refresh. The Host handler now accepts the SignalR query bearer; the posting path refreshes the materialized trial balance on the committed connection before emitting the existing posting topics. Focused ledger tests **88/88** and the post-commit integration contract pass. Final Playwright validation remains blocked by the existing `MoneyInput` controlled-state behavior: the first debit field displays the entered value while focused but reverts to `CHF 0.00` when the sibling line/form rerenders, producing an unbalanced or zero-valued post. The WP remains **verify** pending a focused frontend control fix and a green browser smoke.

- **LL Backend Dev (backend implementation, 2026-07-16):** Implemented the remaining backend/orchestration half. Added `E2ETestAuthenticationHandler` in `Host/Authorization`, accepting only configured `e2e:<member>` bearers and emitting the configured `oid`/`tid`/scope claims; `Program.cs` selects/registers the E2E scheme only when `Environment.IsDevelopment()` **and** `Authentication:E2E:Enabled=true`, otherwise JwtBearer remains the default and the E2E scheme is absent. Development settings define members `a` and `b` plus separate seed subject/tenant pairs. Extended `DevSeed` with idempotent identity-link resolution and Owner memberships for both E2E members, reusing the existing `resolve_identity_link` path. Added `docker-compose.e2e.yml` enabling E2E auth + seed, and `.github/workflows/main.yml` `e2e-smoke` with stack startup, health wait, Chromium install, `--grep @smoke`, artifact upload, cleanup, and deploy dependencies. Added `E2EAuthGatingTests`: scheme absent outside Development/without flag, valid seeded member reaches the existing membership/RLS authorization path, unknown token is rejected. **Validation:** Host build ✅; full Release solution build ✅; E2E auth focus **4/4** ✅; full Release backend suite **351/351** ✅; compose config ✅; diff check ✅; `npm audit --omit=dev --audit-level=high` reports 0 vulnerabilities. Generated OpenAPI and frontend API artifacts unchanged. **Next action:** run `docker compose -f docker-compose.yml -f docker-compose.e2e.yml up -d --build`, then `cd app; npx playwright install chromium; npm run e2e -- --grep @smoke` for final browser certification.

- **LL Frontend Dev (frontend implementation, 2026-07-16):** Implemented the full **frontend** half of the WP. **Seam (D-P3-E2E-AUTH):** new `app/src/application/auth/e2eAuth.ts` (`isE2EAuthEnabled` on build-time `VITE_E2E_AUTH`, `e2eBearerToken` → `e2e:<member>`, synthetic `e2eAccount`, member selected via `localStorage['ll.e2e.member']`, defaults to `a`); wired fail-closed into `acquireApiToken` (returns the test bearer before any MSAL call) and `useAuth` (returns a configured+signed-in synthetic identity) — both branches inert when the flag is unset, so the MSAL path is byte-unchanged. Unit test `e2eAuth.test.ts` pins inert-when-unset, activation for `1`/`true`, `e2e:a` bearer, and account shape. **Harness:** `@playwright/test` devDependency + `e2e` script; `playwright.config.ts` (single worker, `retries: CI?1:0`, screenshot only-on-failure, video retain-on-failure, trace on-first-retry, `webServer` builds+previews with `VITE_E2E_AUTH=1` so Vite inlines the flag); reusable two-browser fixture `app/e2e/fixtures.ts` (`memberA`/`memberB` isolated contexts seeding the member selector via `addInitScript`). **Journeys:** `app/e2e/live-update.spec.ts` (`@smoke` — memberB watches the trial balance, memberA posts a balanced Office-expenses/Bank entry, memberB's debit total live-updates with **no reload** via `expect.poll`) and `app/e2e/smoke.spec.ts` (authenticated render health of home/accounts/trial-balance/new-entry). **Docs:** `app/e2e/test-auth-contract.md` (token/claims/double-gate + backend obligations incl. concrete member subject/tenant GUIDs), `app/e2e/README.md`, `.env.example` gains `VITE_E2E_AUTH=`. **ESLint:** override for `e2e/**` + `playwright.config.ts` (Node globals, relaxed react-refresh/react-hooks). **Gates:** lint ✅, typecheck ✅, vitest **95/95** ✅ (specs correctly excluded — `.spec.ts` outside `src/`), build ✅, page budget ✅ (all four pages ok). *Note:* one shell test asserting "Sign-in not configured" fails **only** with a developer-local `app/.env.local` that supplies a real `VITE_MSAL_CLIENT_ID`; the suite is 95/95 without it (i.e. as CI runs) — pre-existing/environmental, unrelated to this WP, and the frontend seam is inert in that run. **Not done (out of Frontend Dev scope — routed to LL Backend Dev):** `E2ETestAuthenticationHandler` + Program.cs registration (Development ∧ `Authentication:E2E:Enabled`), `DevSeed` second demo-space member for tokens `e2e:a`/`e2e:b`, `appsettings.Development.json` gate, `E2EAuthGatingTests`, `docker-compose.e2e.yml`, and the `main.yml` `e2e-smoke` job. The Playwright journey will authenticate and pass once these land. **Next action:** route the backend E2E half (handler + DevSeed second member + compose/CI job) to LL Backend Dev, then run `npm run e2e` against the full stack to certify the exit criterion.

- **LL Architect (approval + hardening, 2026-07-16):** User **approved the plan and all six decisions on their recommended routes** (no overrides), including the load-bearing, security-sensitive **D-P3-E2E-AUTH**. Folded in four approved hardening additions (Scope §Hardening items 9–12, file list, ACs 9–10): (9) a 5–10-line **test-auth seam contract doc** (`app/e2e/test-auth-contract.md`) pinning the token shape/claims/double-gate for future maintainers; (10) a **reusable two-browser Playwright fixture** in `app/e2e/fixtures.ts` (`memberA`/`memberB` isolated authenticated contexts) so future multi-browser journeys share one reviewed helper; (11) a **CI single-retry policy** (`retries: process.env.CI ? 1 : 0`) for transient transport/browser flakiness only; (12) **screenshot + video + trace on failure** with CI artifact upload for diagnosing a missed live-update. All four are test-infrastructure only — no functional-scope change, no golden fixtures, no accounting consult, no contract/migration change. State → `planned` (unblocked). **Next action:** LL Frontend Dev implements the harness + journey + frontend E2E-auth seam; the backend Development-only `E2ETestAuthenticationHandler` + `DevSeed` second member route to LL Backend Dev or run under explicit in-session user authorization.

- **LL Architect (draft, 2026-07-16):** Plan drafted → state `proposed` → `planned` (awaiting user approval of the six decisions). Researched §1.1/§1.2 (Playwright E2E = main-blocking smoke incl. the two-browser SignalR live-update; PRs stay fast), Part 4 §Phase 3 (the exit criterion), and the WP08 **D-P3-SIGNALR-EXIT** carve-out (browser certification deferred here because Playwright is greenfield). Verified against merged code: no Playwright/`playwright.config`/`e2e/` exists; the WP08 hub + `useSpaceInvalidation` + N6 map + the WP08b `/hubs` WebSocket proxy are the path under test; WP06 posting flow + WP07 trial-balance page are the two UI surfaces; `DevSeed` already provisions the demo space, three CHF accounts, an open period, and Owner membership; the integration `TestAuthHandler` + `resolve_identity_link` are the reusable deterministic-identity shape. Confirmed **spec-derived test infrastructure, no golden fixtures, no accounting consult** (no value computed or crossing any boundary). Identified the load-bearing, security-sensitive **D-P3-E2E-AUTH** (Development-gated, fail-closed test-auth seam on both ends) as the crux, plus five supporting decisions (runner, stack orchestration, two-member users, main-blocking CI placement, single-journey scope). ≤ 2 days; the broader ~15-journey suite is explicitly a future WP. **Next action:** user approves/overrides the six decisions (esp. D-P3-E2E-AUTH), then LL Frontend Dev implements (backend Development-only handler + `DevSeed` second member routed to LL Backend Dev or done under explicit user authorization in-session).
