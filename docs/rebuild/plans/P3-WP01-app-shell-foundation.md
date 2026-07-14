# P3-WP01 — App shell foundation: routing, TanStack Query provider + conventions, error boundaries, desktop-first layout frame

- **Phase:** 3 (frontend re-platform)
- **State:** done — accepted by LL QA Reviewer 2026-07-13.
- **Owner (implementation):** LL Frontend Dev
- **Depends on:** P1-WP04 (the generated OpenAPI TS client + `openapi-fetch` `client.ts` in `app/src/api/**` — the only data-access path; the CI `contract` gate). Nothing else; this WP stands up the shell the rest of Phase 3 mounts pages into.
- **Blocks:** every other Phase-3 WP (WP02 MSAL mounts into the providers; WP03 i18n provider; WP04 primitives render inside the layout; WP05–WP07 pages mount into routes + use the query conventions; WP08 invalidation builds on the WP01 query-key convention).
- **Estimated size:** ≤ 2 days (providers + router + a query-client convention module + a query-key factory + two error boundaries + a desktop-first layout frame + placeholder routes + tests; no backend, no new API calls beyond the existing `getMeta()`).

## Context / re-scope note (LL Architect, 2026-07-12)

Part 4 §Phase 3 bundles the whole frontend re-platform ("app shell: MSAL, generated client, TanStack Query conventions, error boundaries, i18n corpus; port `shared/` primitives; accounts + journal-entry pages; trial-balance report; two-browser SignalR live-update"). Delivered literally that is far beyond ≤ 2 days, so Phase 3 is decomposed into 8 WPs (status.md updated in the same change). **WP01 is the foundation only:** the provider tree, routing, the TanStack Query conventions (client defaults + query-key factory), error boundaries, and the desktop-first layout frame — the scaffold every later page mounts into. Authentication (MSAL) is **WP02**, i18n is **WP03**, primitives are **WP04**, pages are **WP05–WP07**, live invalidation is **WP08**. WP01 ships with placeholder routes and the existing `getMeta()` call as the single proof the generated client flows through the query layer; it adds **no** new API surface.

## Spec sources

- `docs/architecture/rebuild/03-target-architecture.md` §7 (**Frontend structure**: `app/{web,companion,shared}`; flow **features → application → api**; the app layer owns "shell, routing, providers, error boundaries"; **TanStack Query** replaces the DataApi/Dexie layer — "caching, request dedupe, optimistic updates, invalidation keyed by SignalR pings"; **desktop-first** multi-pane/dense layouts + keyboard shortcuts; the old `useTableBreakpoint` responsive-table machinery is **not** ported; PWA = manifest + static-shell precache only, offline = read-only "you're offline" state), §9 (**Errors:** Result-based domain errors → ProblemDetails; global exception handler; **React error boundary per route + top level**; observability — correlation id per request client → API → SQL).
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 3 (app shell: generated client, TanStack Query conventions, error boundaries; decomposed under page budgets), §3 (frontend dependency graph: **features → application(TanStack Query) → api(generated) — no other path**), §5 (salvage vs rewrite: "React UI primitives, feature-policy matrix, i18n corpus, MSAL flow shape" salvage; "all data access Dexie → TanStack Query + generated client" rewrite).
- `docs/rebuild/plans/NOTES-risk-review-2026-07-06.md` — **N6** (TanStack Query invalidation map is an explicit, reviewable per-module deliverable): WP01 lays its foundation by establishing the **query-key factory convention** the map keys off; the map document + cache-correctness tests themselves land in **WP08**.
- `.github/instructions/frontend.instructions.md` (layering/boundaries enforced by ESLint; **page budget 450 lines / 30 imports / 20 state hooks**, error at 550/40/28; desktop-first, reuse shared primitives, i18n-all-locales, minor-units-only, client validation UX-only; "done means" lint/typecheck/test/page-budget green).
- ADR-0001 (online-first; server/DB are the source of truth — the client owns no schema), ADR-0003 (API contract pipeline — the generated client is the only data path).

## Goal

Stand up the **web app shell**: a single provider tree (`QueryClientProvider` + a router + a top-level error boundary), a documented **TanStack Query conventions module** (a shared `QueryClient` with explicit defaults + a typed **query-key factory**), **per-route error boundaries** with a fallback UI, and a **desktop-first layout frame** (nav rail + content pane, keyboard-focusable) into which every later Phase-3 page mounts. Prove the generated client flows end-to-end through the query layer by rendering the existing `getMeta()` result on a placeholder route. Establish the boundaries (features → application → api) as real, enforced structure — not just lint config — so WP02–WP08 have a correct scaffold and never reach for `fetch` or the app shell from a feature.

## Scope

1. **Provider tree (`app/src/app/`):**
   - A single composed provider root: `QueryClientProvider` (using the shared client from the conventions module) → top-level `AppErrorBoundary` → `RouterProvider`. Mounted from `main.tsx` (replacing the current bare `<App/>`).
   - The provider composition is a small, testable unit (a `Providers` component or `createAppRouter()` + `AppRoot`), not inlined logic in `main.tsx`.
2. **TanStack Query conventions (`app/src/application/query/`):**
   - `queryClient.ts` — a factory returning a `QueryClient` with **explicit, documented defaults**: `staleTime`, `gcTime`, `retry` policy (no retry on 4xx; bounded retry on 5xx/network), `refetchOnWindowFocus`, and a default `throwOnError` posture consistent with the route error boundaries. Every default is a deliberate, commented decision, not a library default.
   - `queryKeys.ts` — a **typed query-key factory** (structured, hierarchical keys, e.g. `qk.reports.trialBalance(spaceId)`, `qk.accounts.list(spaceId)`, `qk.meta()`), the single source of query keys. This is the **N6 foundation**: the WP08 invalidation map keys off these factories, and no feature hand-writes raw key arrays. WP01 seeds only the keys it needs (`meta`) plus documented placeholders for the WP05–WP07 keys, so the convention is established.
   - A short conventions doc (`app/src/application/query/README.md`) stating the defaults, the key-factory rule, and the "mutations invalidate via the key factory, never raw arrays" convention (the reviewable seam N6 completes in WP08).
3. **Routing (`app/src/app/`):**
   - A router (library per **D-P3-ROUTER**) with: a root layout route rendering the layout frame, at least a **placeholder dashboard/home** route (renders `getMeta()` via a query hook), and a **not-found (404)** route. Routes are lazy-mountable so later pages code-split.
   - Route definitions live in the app-shell layer; **feature pages** (WP05+) will register under it via the features layer — WP01 provides the seam, not the pages.
4. **Error boundaries (`app/src/app/`):**
   - `AppErrorBoundary` (top level) — catches provider/render failures, shows a minimal app-level fallback, logs the error (with a correlation hook seam for later OTel wiring per §9; no OTel SDK in WP01).
   - `RouteErrorBoundary` (per route) — wraps each route element; shows a route-scoped fallback with a "retry"/"go home" affordance and surfaces ProblemDetails shape when the error is an API error (the generated client's error type), without leaking stack traces to the UI.
5. **Desktop-first layout frame (`app/src/app/`):**
   - `AppLayout` — a multi-pane desktop frame (nav rail/sidebar + top bar slot + main content pane), keyboard-focusable, using CSS custom properties for tokens (real token module arrives in WP04; WP01 uses a minimal token seam / placeholders, no hard-coded hex where a token will exist). **No** mobile responsive-table machinery, **no** `useTableBreakpoint`-style hooks (explicit non-port).
6. **`getMeta()` proof-of-wiring:**
   - A `useMeta()` query hook in the application layer calling the existing `getMeta()` (P1-WP04), consumed by the placeholder home route via the query client — proving generated-client → application(query) → feature flow works through the real provider tree. Reuses the existing `application/meta.ts`; no new endpoint.
7. **Boundary + budget conformance:**
   - The existing ESLint boundary rules (features → application → api; application must not import features/app-shell; data access only via `src/api`) stay green with the new folders populated. Page-budget gate green for every new file.

## Non-goals (explicitly deferred)

- **No authentication — WP02.** No MSAL, no token acquisition, no bearer attachment, no sign-out; the `getMeta()` endpoint is unauthenticated (P1-WP04) so WP01 needs none. The provider tree leaves a documented slot where the MSAL provider mounts.
- **No i18n — WP03.** WP01 may use a tiny hard-coded English placeholder string set **only** in shell chrome that WP03 will replace, or (preferred) stub the i18n provider seam; it does **not** import the corpus or add locale files. (Note: this is the one place WP01 is permitted temporary non-i18n copy — flagged so QA does not treat it as a violation; WP03 removes it.)
- **No shared UI primitives / design system — WP04.** No DataTable/pickers/modals/form primitives, no full token module; WP01 uses only the minimal layout chrome it needs, with a token seam.
- **No pages / real data — WP05–WP07.** No accounts, journal-entry, or report pages; only placeholder + not-found routes. No new API calls beyond `getMeta()`.
- **No SignalR / live invalidation — WP08.** WP01 establishes the query-key factory (the N6 foundation) but ships **no** invalidation map document, no SignalR client, no coalescing.
- **No PWA/service-worker/manifest work.** Manifest + static-shell precache is a later shell concern; not in WP01.
- **No `app/web` / `app/shared` / `app/companion` restructure** unless **D-P3-STRUCT** is approved for the restructure route (see Decisions). Default recommendation is to keep the current flat `app/src` for Phase 3.
- **No backend change, no OpenAPI/TS regeneration** (no new/changed endpoints). `app/src/api/**` is not hand-edited.

## Decisions (front-loaded, non-accounting — approved 2026-07-13)

These are load-bearing structural choices with no accounting content; per the WP12/WP13 precedent they are surfaced for a one-time user decision so implementation is unambiguous.

- **D-P3-STRUCT — app folder layout for Phase 3 — approved.** The §7 target is `app/{web,companion,shared}/`, but the **companion is M3** and no shared-with-companion code exists yet. Restructuring `app/src` → `app/web/src` + extracting `app/shared` now churns the P1-WP04 generated-client output path, the ESLint boundary config, `vite.config.ts`/`vitest.config.ts`, `tools/check-page-budget.cjs` paths, and the CI `contract`/frontend job paths — for **no M1 benefit**.
   - **Accepted route:** **keep the flat `app/src/{api,application,features,app}`** for Phase 3; introduce `app/web` + `app/shared` only when the companion (M3) actually needs shared code, recorded then as an ADR. WP01 proceeds in `app/src`.
  - **Alternative:** restructure to `app/web` + `app/shared` now → then WP01's first task is the move + updating all tooling/CI paths, recorded as **candidate ADR-0007**, and every Phase-3 path in this plan shifts under `app/web/src`.
- **D-P3-ROUTER — routing library — approved.** Use **React Router (data router)**. It is mature, matches the OLD app's routing shape, and its native per-route `errorElement` maps cleanly onto the §9 "error boundary per route" requirement. The new production dependency is recorded here before use.
- **D-P3-QUERYKEYS — query-key factory convention — approved.** Adopt a single typed **query-key factory** (`qk.<module>.<query>(...args)`) as the only source of query keys, which the WP08 invalidation map (N6) keys off. The module-namespaced factory shape is the required convention for WP05–WP08.

## Accounting decisions

**None.** App-shell plumbing is not accounting behavior. No golden fixtures, no LL Accounting Expert consult. (Recorded explicitly so QA does not expect an accounting artifact.)

## Golden fixtures

**None required.** WP01 produces no financial output and ports no accounting rule. Shell behavior (routing, error boundaries, query defaults) is pinned by component/unit tests (the correct tier), not golden fixtures. Client-side *validation* mirroring (which is fixture-pinned) begins in **WP06**, not here.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

### Reference only (shape salvage, not code)
- **OLD app shell / providers / routing** (`src/main.tsx`, `src/App.tsx`, the router setup, `AuthContext`/providers) — used to confirm the *provider-composition order and route surface*; the OLD Dexie/DataApi/`RoleProvider` wiring is **not** ported (data access is rewritten to TanStack Query + generated client per §5). MSAL flow *shape* is salvaged in **WP02**, not here.
- **OLD error handling / layout chrome** — the desktop layout intent (nav + panes) is reused as a design reference; the responsive-table (`useTableBreakpoint`) machinery is an explicit **non-port** (§7 desktop-first).

### Rewrite (spec-derived; no OLD oracle)
- The `QueryClient` conventions, the query-key factory, the two error boundaries, and the desktop-first `AppLayout` are greenfield per §7/§9, pinned by component/unit tests.

## Dependencies

- **New production dependencies (frontend, recorded here before use):** `@tanstack/react-query` (the core of the §7 data layer) and the router chosen in **D-P3-ROUTER** (default `react-router` / `react-router-dom`). `@tanstack/react-query-devtools` is a dev-only optional addition. No other new production deps in WP01. Any addition beyond these is a plan amendment.
- **Test tooling:** the existing Vitest + Testing Library stack (extend with `@testing-library/react` if not already present — dev dependency, recorded here). No E2E/Playwright in WP01 (Playwright journeys land with the pages/exit gate).
- Node/npm workspace + `app/` scripts (`lint`, `typecheck`, `test`, `check:page-budget`) already exist (P1-WP01/WP04).

## File list (implementation target — assumes D-P3-STRUCT default: flat `app/src`)

**New — `app/src/application/query/`**
- `queryClient.ts` — shared `QueryClient` factory with documented defaults (retry/staleTime/gcTime/refetchOnWindowFocus/throwOnError).
- `queryKeys.ts` — typed query-key factory (`qk`), seeded with `meta` + documented placeholders for later modules.
- `useMeta.ts` — `useMeta()` query hook wrapping the existing `getMeta()` (`application/meta.ts`).
- `README.md` — query conventions (defaults, key-factory rule, "invalidate via the factory" convention — N6 foundation).

**New — `app/src/app/`**
- `providers.tsx` (or `AppRoot.tsx`) — composed provider tree (`QueryClientProvider` → `AppErrorBoundary` → `RouterProvider`), with a documented mount slot for the WP02 MSAL provider and WP03 i18n provider.
- `router.tsx` — route definitions (root layout + placeholder home + not-found), lazy-mountable.
- `AppLayout.tsx` — desktop-first frame (nav rail + top-bar slot + content pane), keyboard-focusable, token-seam styling.
- `AppErrorBoundary.tsx` — top-level boundary + fallback + error-log seam.
- `RouteErrorBoundary.tsx` — per-route boundary + fallback (retry/go-home), ProblemDetails-aware, no stack-trace leak.
- Placeholder route components (`HomeRoute.tsx` rendering `useMeta()`, `NotFoundRoute.tsx`) — under `features/` if they are feature pages, or `app/` if they are shell chrome; kept trivially small (real pages are WP05+).

**Modified**
- `app/src/main.tsx` — mount the composed provider root instead of the bare `<App/>`.
- `app/src/app/App.tsx` — becomes the layout/shell entry consumed by the router (or is folded into `AppLayout`; keep one clear shell entry).
- `app/package.json` — add the recorded production deps (`@tanstack/react-query`, router) + dev test deps; new/verified scripts if needed.
- `app/eslint.config.js` — only if the new `application/query` + `app/` folders need boundary-rule coverage confirmed (rules must stay green; no loosening).
- `app/src/index.css` (or a tokens seam file) — minimal layout token custom-properties placeholders (real tokens in WP04).
- `docs/rebuild/plans/P3-WP01-app-shell-foundation.md` + `docs/rebuild/status.md` — notes/state.

**New — tests (`app/src/**` colocated or `app/tests/`)**
- Provider/router render tests; error-boundary tests (a throwing component → route fallback shown, error logged; top-level boundary catches a provider error).
- `queryClient` config test (asserts the documented defaults — retry-not-on-4xx, staleTime, refetchOnWindowFocus).
- `queryKeys` factory test (stable, structured keys; no collisions between modules).
- `useMeta()` test (maps `getMeta()` through the query hook; handles error → surfaced via boundary/state).
- Layout test (renders nav + content pane; no responsive-table/`useTableBreakpoint` import).

No changes under `app/src/api/**` (generated), no backend files, no `backend/openapi/**` regeneration, no new migration.

## Acceptance criteria (concrete, testable)

1. **Boots + routes.** `main.tsx` mounts the composed provider tree; the app renders the home route and navigates to a not-found route for an unknown path — proven by a router render/navigation test.
2. **Query client defaults are explicit.** A config test asserts the shared `QueryClient`'s documented defaults: **no retry on 4xx**, bounded retry on 5xx/network, a defined `staleTime`/`gcTime`, and the chosen `refetchOnWindowFocus` posture — none left to library default.
3. **Query-key factory is the single key source.** A test asserts `qk` produces stable, structured, collision-free keys (`meta` + at least the documented placeholders); the conventions README documents the "invalidate via the factory only" rule (N6 foundation).
4. **`getMeta()` flows through the query layer.** The home route renders the meta response obtained via `useMeta()` through the real `QueryClientProvider` — proven by a test that mocks the generated client and asserts the value renders (generated-client → application → feature path exercised).
5. **Per-route error boundary works.** A route whose element throws renders the `RouteErrorBoundary` fallback (with a retry/go-home affordance) instead of a white screen; an API-error (ProblemDetails-shaped) renders a message without leaking a stack trace — both proven by tests.
6. **Top-level error boundary works.** A failure at the provider/app level renders the `AppErrorBoundary` fallback and invokes the error-log seam — proven by a test.
7. **Desktop-first layout, no mobile machinery.** `AppLayout` renders a nav rail + content pane and is keyboard-focusable; a test/grep asserts **no** `useTableBreakpoint`-style responsive-table hook or import exists in the shell.
8. **Boundaries enforced.** `npm run lint` passes with the boundary rules intact: a proof (test or a temporary disallowed import reverted) shows a feature importing `src/api` or the app shell fails lint; the application/query layer imports neither features nor the app shell.
9. **Page budget green.** `npm run check:page-budget` passes for every new file (all well under 450 lines / 30 imports / 20 state hooks); no `JournalEntryPage`-scale file introduced.
10. **All gates green, generated client untouched.** `npm run lint`, `npm run typecheck`, `npm test`, and `npm run check:page-budget` are green; `app/src/api/**` is byte-unchanged (no hand-edit); `npm audit` shows no new vulnerabilities from the added deps; the CI `contract` gate is unaffected (no backend/OpenAPI change).

## Boundary note

WP01 respects the frontend layering (**features → application → api**) and the §5 module graph. It adds no cross-layer path: the app-shell layer owns providers/router/boundaries/layout; the application layer owns the query client/keys/hooks and imports only `api` + shared utilities; feature/route components import only the application layer and local files. No feature or application code imports the app shell; no data access bypasses the generated client. The new production dependencies (`@tanstack/react-query`, the chosen router) are recorded above before use per the no-undocumented-dependency rule.

## Open questions / carry-forwards

- **D-P3-STRUCT / D-P3-ROUTER / D-P3-QUERYKEYS** — approved 2026-07-13 as the recommended routes above. The flat `app/src` layout, React Router data router, and module-namespaced typed query-key factory are now implementation constraints for WP01.
- **N6 completion** — the invalidation *map document* + cache-correctness tests land in **WP08**; WP01 only establishes the query-key factory it keys off. Recorded so QA does not expect the map in WP01.
- **OTel/correlation-id** — WP01 leaves an error-log seam in the boundaries; the real OpenTelemetry SDK + correlation-id propagation (§9) is a later observability WP, not WP01.
- **MSAL provider slot** — WP01 documents where the WP02 MSAL provider mounts; if WP02 needs the provider *above* the router (redirect handling), WP01's provider composition must leave that seam — flagged for the WP02 planner.

## Implementation log

- **2026-07-13 — LL Frontend Dev:** Started implementation on the approved flat `app/src` route. Added the TanStack Query client defaults and typed `qk` factory, `useMeta()` proof hook, React Router data-router shell with lazy home route and 404 route, top-level and route error fallbacks, keyboard-focusable desktop layout frame, and query convention tests. Added `@tanstack/react-query`, `react-router-dom`, `@testing-library/react`, `@testing-library/dom`, and `jsdom`; generated API files and backend/OpenAPI files remain untouched. Added DOM coverage for home/404 routing, query-rendered metadata, layout focusability, generic and ProblemDetails route failures, and top-level error logging. Validation: lint green; Vitest **12/12**; page budget green; typecheck/production build green; production audit reports 0 vulnerabilities. Next: independently verify the full WP acceptance matrix.
## QA Verdict

**PASS**

1. **Diff vs plan**: All acceptance criteria are met. No files were modified outside of the plan scope (`app/src/api` was untouched as evidenced by `git diff -- src/api`). Scope creep was completely avoided.
2. **Tests**: All tests are green:
   - `npm run test`: 12/12 frontend tests pass independently, including DOM layout, routing mocks, and error boundaries.
   - `npm run typecheck`, `npm run lint`, and `npm run build` executed successfully without warnings.
   - `npm run check:page-budget`: Passed successfully for all new architectural components.
3. **Financial integrity**: Not an accounting WP, but no financial shortcuts or deviations regarding display were inadvertently hard-coded.
4. **Security**: Security posture checked. `npm audit --omit=dev` yielded zero vulnerabilities for newly added prod deps. No secrets leak. `AppErrorBoundary` avoids leaking stacktraces into the UI, exposing structured `ProblemDetails` correctly.
5. **Hallucination scan**: No unrequested libraries or features were invented. The query implementation matches TanStack strictly. `useTableBreakpoint` wasn't ported (verified with select string check).
6. **Patch-layering scan**: Error boundaries use correct `render(): never` signatures required by React 19 testing. Traps are strongly typed around actual object schemas.

**Merged:** PR #26 merged to `main` on 2026-07-13. WP01 is complete on the default branch; the next planned work is P3-WP02 (MSAL authentication).

## Documentation Log

- **2026-07-13 — LL Docs Editor:** Recorded PR #26 as merged to `main`. P3-WP01 remains `done`; the app-shell foundation is available for the remaining Phase-3 work packages. Next: P3-WP02 (MSAL authentication).
