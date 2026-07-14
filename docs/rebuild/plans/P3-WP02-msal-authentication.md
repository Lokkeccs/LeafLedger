# P3-WP02 — MSAL authentication (`sessionStorage`) + bearer-token wiring + sign-out clears all app state

- **Phase:** 3 (frontend re-platform)
- **State:** done — **approved by the user 2026-07-13** with **D-P3-MSAL-FLOW = popup** (overriding the recommended redirect); D-P3-MSAL-LIB / -BEARER / -SIGNOUT accepted on their recommended routes; D-P3-MSAL-REG accepted mock-verified (no client id supplied → live E2E sign-in deferred to the WP08 exit gate). QA re-review passed after the three follow-up acceptance-coverage gaps were fixed.
- **Owner (implementation):** LL Frontend Dev
- **Depends on:**
  - **P3-WP01** (done) — the app-shell provider tree (`AppRoot` = `QueryClientProvider` → `AppErrorBoundary` → `RouterProvider`), the shared `QueryClient`, the query-key factory, and the documented "MSAL provider slot" carry-forward. WP02 mounts the MSAL provider into that slot.
  - **P2-WP11** (done) — real Entra `common` token validation on the Host (JWKS signature + audience allowlist `api://leafledger` + issuer-pattern bound to `tid`; `RequiredScope = ledger.write`). WP02 produces the tokens WP11 validates.
  - **P2-WP13** (done) — identity-links resolver + link-only first-contact provisioning. The stale-space auto-join class is **structurally** eliminated server-side; WP02 must not re-introduce it on the client (no `upsertMicrosoftUser`-style auto-join port).
- **Blocks:** every authenticated Phase-3 page — WP05 (accounts read), WP06 (journal-entry posting), WP07 (trial-balance report) all call space-scoped, `ledger.*`-gated endpoints and need a bearer token attached to the generated client. WP08 (SignalR) needs an access token for the hub connection.
- **Estimated size:** ≤ 2 days (MSAL config module + provider mount + `initialize()` gate + a bearer-attach middleware on the generated client + a sign-out state-teardown action + a signed-in/signed-out gate in the shell + mock-based unit/integration tests; **no backend, no OpenAPI/TS regeneration, no new API surface**).

## Context / re-scope note (LL Architect, 2026-07-13)

Part 4 §Phase 3 bundles the whole frontend re-platform; Phase 3 was decomposed into 8 WPs (P3-WP01…08) on 2026-07-12. **WP02 is the authentication slice only:** stand up MSAL against Entra `common` with the cache in `sessionStorage`, wire acquired access tokens as bearer tokens onto the P1-WP04 generated client, and make sign-out clear **all** client app state (the §8.1 "kills the stale-space auto-join class" requirement — on the client this means no auto-provisioning port + a full cache/state teardown). i18n is **WP03**, primitives are **WP04**, pages are **WP05–WP07**, live invalidation is **WP08**. WP02 adds **no** page and **no** new endpoint call beyond the existing unauthenticated `getMeta()` (used to prove the anonymous path still works and the authenticated path attaches a token).

The §8.1 clause "MSAL cache in `sessionStorage`; sign-out clears all app state" was explicitly deferred here from the P2-WP11 plan (backend is stateless bearer auth — no server session to clear; the client owns this). WP02 delivers it.

## Spec sources

- `docs/architecture/rebuild/03-target-architecture.md` §8.1 (**AuthN:** "Entra `common`, audience + signature + issuer-pattern allowlist; **MSAL cache in `sessionStorage`**; **sign-out clears all app state (kills the stale-space auto-join class)**"), §2 salvage table line 51 (**MSAL / Entra `common` = Keep**; "Cache moved to `sessionStorage`; identity links table replaces runtime candidate guessing"), §7 (frontend structure: **features → application → api**; the app layer owns "shell, routing, providers, error boundaries"; `shared/` holds "auth glue"), the diagram note "HTTPS (**JWT bearer**, idempotency keys on writes)".
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 3 ("New app shell: **MSAL (sessionStorage)**, generated client, TanStack Query conventions…"), §5 salvage ("React UI primitives, feature-policy matrix, i18n corpus, **MSAL flow shape**" = salvage the *shape*; "**all data access** Dexie → TanStack Query + generated client" = rewrite).
- `docs/rebuild/plans/P2-WP11-authn-token-validation.md` — the server accept/reject matrix WP02's tokens must satisfy: audience `api://leafledger`, `common`/v2.0 issuer bound to `tid`, `RequiredScope = ledger.write` (so the SPA must request scope `api://leafledger/ledger.write`).
- `docs/rebuild/plans/P2-WP13-identity-links.md` — first contact provisions a **link only**, never a membership; the client must not port the OLD `upsertMicrosoftUser` auto-join.
- `docs/rebuild/plans/P3-WP01-app-shell-foundation.md` "Open questions / carry-forwards → MSAL provider slot": WP01 left the provider-composition seam; if WP02 needs the MSAL provider **above** the router (redirect handling), WP01's composition must be extended — it is (see Scope §2).
- `.github/instructions/frontend.instructions.md` (layering enforced by ESLint; **page budget** 450 lines / 30 imports / 20 state hooks; "done means" lint/typecheck/test/page-budget green; minor-units-only; client validation UX-only — N/A here, no accounting surface).
- ADR-0001 (online-first; the server/DB are the source of truth), ADR-0003 (the generated client is the only data path — bearer attaches **at** that client).

## Goal

Sign a user in against **Entra `common`** using MSAL with the token cache in **`sessionStorage`**, attach the acquired API access token as an `Authorization: Bearer` header on the **single generated `openapi-fetch` client** (so every WP05–WP08 call is authenticated with no per-call plumbing), and make **sign-out clear all client app state** — MSAL cache teardown **plus** the TanStack Query cache **plus** any in-memory auth/space state — so no stale-space data or identity survives a sign-out. The MSAL *silent-then-interactive* shape is salvaged from the OLD app (`acquireTokenSilent` with interactive fallback), but WP02 uses the **popup** flow (`loginPopup`/`acquireTokenPopup`/`logoutPopup`, D-P3-MSAL-FLOW) rather than the OLD redirect flow, so no pre-mount redirect-ordering gate is needed — only a plain `initialize()` gate. The OLD `localStorage` cache, the bespoke `AuthContext`, the Dexie/`upsertMicrosoftUser` auto-provisioning, and the Graph-photo/rights coupling are **not** ported. Everything is proven with an MSAL **mock** (no live IdP in CI); a live end-to-end sign-in needs an Entra SPA app registration whose provisioning is routed to the user (D-P3-MSAL-REG).

## Scope

1. **MSAL configuration module (`app/src/application/auth/` — the "auth glue"):**
   - An MSAL `Configuration` with `auth.authority = https://login.microsoftonline.com/common`, `auth.clientId` / `auth.redirectUri` / `auth.postLogoutRedirectUri` read from **build-time `VITE_` env** (client id and redirect URIs are public identifiers, **not secrets** — no secret enters the repo), and **`cache.cacheLocation = 'sessionStorage'`** (the load-bearing §8.1 change vs the OLD `localStorage`).
   - Exported scope constants matching the WP11 server contract: the interactive login scope set and the API scope `api://leafledger/ledger.write` (read from env with that default), so acquired tokens carry audience `api://leafledger` + scope `ledger.write`.
   - A configured-vs-not guard (`hasClientId()`), so a deployment without the registration shows a clear "sign-in not configured" state rather than a crash (OLD `getMicrosoftClientIdErrorMessage` shape).
2. **Provider mount + initialization gate (`app/src/app/`):**
   - Extend the WP01 `AppRoot` to mount the MSAL provider (per D-P3-MSAL-LIB) **above** the `RouterProvider`. An **initialization gate** renders a minimal splash until `msalInstance.initialize()` resolves, then renders the shell. **Popup flow (D-P3-MSAL-FLOW) needs no pre-mount redirect processing** — the OLD `primeAuthentication()`/`handleRedirectPromise`-before-mount ordering concern (a redirect strips the `?code=` param if React Router runs first) does **not** apply to popup, since the auth code round-trips inside the popup window and never touches the main-window URL; `initialize()` is still required before any MSAL call.
   - The composition order becomes: MSAL provider → `QueryClientProvider` → `AppErrorBoundary` → `RouterProvider` (the WP01 slot is filled; the WP03 i18n provider slot is preserved). MSAL stays above the router so `useMsal()` is available shell-wide.
3. **Bearer-attach on the generated client (`app/src/application/auth/` + `app/src/api/client.ts`):**
   - Register an `openapi-fetch` **middleware** (`apiClient.use({ onRequest })`) that, for each request, obtains an access token via `acquireTokenSilent` (interactive fallback per §7 below) and sets `Authorization: Bearer <token>` — the single, DRY seam so no feature touches headers. `app/src/api/client.ts` is **hand-authored** (P1-WP04), not generated (`schema.d.ts` is the generated file); the middleware registration lives in the application-layer auth module and is attached to the exported client, keeping `client.ts` a thin factory.
   - **Anonymous requests still work:** when no account is signed in (or the endpoint is anonymous, e.g. `getMeta()`), the middleware attaches no header and the request proceeds — proven against the existing `useMeta()` path.
4. **Sign-in / sign-out actions + shell gate (`app/src/application/auth/` + `app/src/app/`):**
   - `signIn()` = `loginPopup({ scopes: loginScopes })` (D-P3-MSAL-FLOW = popup); `signOut()` = `logoutPopup(...)` **and** `queryClient.clear()` **and** drop in-memory auth/space state — the full §8.1 teardown. Because the cache is `sessionStorage`, tab-close already clears MSAL state; explicit sign-out additionally clears the TanStack Query cache so **no stale-space data survives**. (`logoutPopup` keeps the SPA mounted and clears the sign-out target's account; the full clear still runs regardless of the popup outcome.)
   - A small `useAuth()` hook (account, `isSignedIn`, `signIn`, `signOut`) and a shell affordance (sign-in button when signed out, account/sign-out when signed in) in the layout top-bar slot. Copy uses the WP01 temporary-English placeholder convention (WP03 i18n replaces it) — flagged so QA does not treat it as an i18n violation.
   - **Popup blockers:** `loginPopup`/`acquireTokenPopup` must originate from the user gesture (the sign-in click) so the browser does not block the window; a blocked/closed popup surfaces a clear, retryable message (no silent failure, no loop).
5. **Silent-token acquisition with bounded interactive fallback:**
   - `acquireTokenSilent` first; on `InteractionRequiredAuthError`, fall back to `acquireTokenPopup` (D-P3-MSAL-FLOW = popup) with a throttle so a failing scope cannot spawn a tight popup/iframe loop (the OLD `consentAttemptByScope` lesson) — no infinite loop. Note the background-token path (the `openapi-fetch` middleware) has **no** user gesture, so a popup there may be blocked: the middleware falls back at most once (throttled) and otherwise surfaces an "interaction required, please retry" error rather than spawning popups per request.
6. **Explicit non-port of the auto-join class:**
   - The OLD `AuthContext` called `upsertMicrosoftUser(...)` on session restore (the stale-space auto-join weakness, §2.5). WP02 **must not** port any client-side membership/space auto-provisioning. First contact and membership are server concerns (WP13 link-only). A test/grep asserts no such call exists.
7. **Boundary + budget conformance:**
   - Auth glue lives in the application layer (`application/auth/`) + the app-shell layer (provider mount, shell gate); **no feature** imports MSAL or reaches for a token directly (they call the generated client, which is already authenticated). ESLint boundary rules and the page budget stay green for every new file.

## Non-goals (explicitly deferred)

- **No i18n — WP03.** WP02 may use the WP01-sanctioned temporary English placeholders in the sign-in/out chrome only; it imports no corpus and adds no locale files. (Flagged so QA does not treat it as a violation; WP03 removes it.)
- **No shared UI primitives / design system — WP04.** The sign-in affordance is minimal shell chrome, not a designed button/modal component. No SignInModal/ModalShell port.
- **No pages / real authenticated data — WP05–WP07.** No accounts/journal/report pages. The only wired call is the existing `getMeta()`, used to prove the anonymous path and (with a mock account) the bearer-attach path. No new endpoint, no space-scoped read yet.
- **No SignalR / hub token — WP08.** WP08 consumes the token to authenticate the hub; WP02 only makes token acquisition available.
- **No license entitlement UI.** Licensing is a server seam (`ILicenseEntitlement`, allow-all default per WP06; real enforcement = Licensing module). WP02 renders nothing license-specific.
- **No identity-links / membership client logic.** Resolution + provisioning are server-side (WP11/WP13, done). The client sends a bearer token and reads whatever the server authorizes; it does **not** replicate candidate-guessing or provisioning.
- **No Graph profile photo / rights fetch port.** The OLD `AuthContext` fetched a Graph photo and per-user rights (client-side authZ = a "Replace" verdict). WP02 ports neither; a display name from the token account is sufficient chrome.
- **No companion app, no PWA/service-worker/auth-in-SW work.**
- **No backend change, no OpenAPI/TS regeneration.** `app/src/api/schema.d.ts` (generated) is byte-unchanged; the bearer scheme is already in the WP06/WP11 OpenAPI. The CI `contract` gate is unaffected.

## Decisions (front-loaded, non-accounting — route to user before implementation)

Per the WP01/WP12/WP13 precedent, load-bearing structural choices with no accounting content are surfaced for a one-time user decision so implementation is unambiguous.

- **D-P3-MSAL-REG — Entra SPA app registration (blocking for live sign-in, not for the WP).** MSAL needs an Entra **SPA app registration**: a public **client id**, **redirect URIs** (`http://localhost:5173` for dev + the deployed origin), and the API scope `api://leafledger/ledger.write` (matching the WP11 server audience/scope) granted to the SPA. These are **public identifiers, not secrets** — supplied as build-time `VITE_` env, `.env.example` documented, nothing committed.
  - **Recommended route:** the user provisions the SPA registration and supplies the client id (and confirms redirect URIs + API-scope grant) so a **live** sign-in can be smoke-tested; **all** unit/integration tests use an MSAL mock and need no registration, so implementation is **not blocked** on it. If the registration is not ready, WP02 ships fully mock-verified and the **live E2E sign-in journey is deferred to the WP08 exit gate** (which already owns the two-browser live journey).
  - **User action:** provide the client id + confirm redirect URIs/API-scope grant **now** (enables live smoke), or approve deferring live E2E to WP08 (implementation proceeds either way).
- **D-P3-MSAL-LIB — MSAL React binding — recommended `@azure/msal-react` + `@azure/msal-browser`.** The official `@azure/msal-react` (`MsalProvider`, `useMsal`, `useIsAuthenticated`, `useAccount`) replaces the OLD ~450-line bespoke `AuthContext`, handles redirect processing and account state, and pairs with `@azure/msal-browser`. Alternative: a thin bespoke context over `@azure/msal-browser` only (OLD shape, fewer deps, more hand-rolled code). **Recommend `@azure/msal-react`** (less custom auth code = fewer places to get security wrong). Both new production deps are recorded in Dependencies before use.
- **D-P3-MSAL-FLOW — interactive flow — CHOSEN: popup (user decision 2026-07-13, overriding the recommended redirect).** Use **popup** (`loginPopup`/`acquireTokenPopup`/`logoutPopup`). Consequences accepted: interactive calls must fire from a user gesture (the sign-in click) or the browser blocks the popup; the background bearer middleware cannot open a popup silently, so on `InteractionRequiredAuthError` it surfaces a retryable "interaction required" error (throttled, no per-request popups) instead of forcing re-auth. Benefits: no full-page navigation, the SPA/router state is never torn down mid-flow, and the pre-mount redirect-ordering gate collapses to a plain `initialize()` gate. (The recommended alternative was redirect — matching the OLD `loginRedirect` shape and slightly more robust under aggressive popup blockers / MSIX; not chosen.)
- **D-P3-MSAL-BEARER — token attach point — recommended `openapi-fetch` `onRequest` middleware.** Attach the bearer via a single `apiClient.use({ onRequest })` middleware that calls `acquireTokenSilent` (interactive fallback), so the generated client is the DRY authenticated seam (ADR-0003). Alternative: a per-hook token argument threaded through the application layer (repetitive, error-prone). **Recommend the middleware.**
- **D-P3-MSAL-SIGNOUT — sign-out teardown — recommended full clear.** `signOut()` = `logoutRedirect` **+** `queryClient.clear()` **+** drop in-memory auth/space state, satisfying "sign-out clears all app state (kills the stale-space auto-join class)". Combined with `sessionStorage` (tab-close clears MSAL) and the non-port of `upsertMicrosoftUser`, the stale-space auto-join class cannot recur on the client. **Recommend the full clear.**

## Accounting decisions

**None.** Authentication is access control, not accounting behavior. No LL Accounting Expert consult. (Recorded explicitly so QA does not expect an accounting artifact.)

## Golden fixtures

**None required.** WP02 produces no financial output and ports no accounting rule. Auth flow (config shape, provider gate, bearer attach, sign-out teardown, interactive-fallback throttle) is pinned by **component/unit/integration tests with an MSAL mock** — the correct tier. (The OLD `authRedirectFlow.test.ts` is the shape reference for how to mock `@azure/msal-browser`.)

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

### Reference only (flow shape salvage, not code)
- `src/auth/msalConfig.ts` (`MSAL_CONFIG`, `LOGIN_SCOPES`, `API_*_SCOPE`) — the **config shape** is reused, but `cacheLocation` moves `localStorage → sessionStorage`, the client id/authority/scope are env-driven, and the API scope becomes `api://leafledger/ledger.write` (new backend), **not** the OLD `sync.readwrite`.
- `src/auth/AuthContext.tsx` (`getAccessToken = acquireTokenSilent` + `InteractionRequiredAuthError` fallback + per-scope throttle; the OLD `acquireTokenPopup` incremental-consent path is the closer analogue for WP02's chosen popup flow) — the **silent-then-interactive** shape is salvaged; the bespoke context, the `localStorage` user cache, the Dexie `upsertMicrosoftUser` auto-join, the Graph photo, and the `getCurrentUserRole/Rights` client-authZ coupling are **not** ported. WP02 diverges from the OLD `loginRedirect`/`logoutRedirect` to **popup** (D-P3-MSAL-FLOW).
- `src/main.tsx` `bootstrap()` (await redirect processing before React mounts) — a **redirect-specific** ordering lesson that **does not apply** to WP02's popup flow; WP02 keeps only the plain `initialize()`-before-use requirement. The service-worker/i18n wiring is not part of WP02.
- `tests/authRedirectFlow.test.ts` — the **mock pattern** for `@azure/msal-browser` in Vitest is the testing reference.

### Rewrite (spec-derived; no OLD oracle)
- The `sessionStorage` config, the `@azure/msal-react` provider mount + `initialize()` gate, the popup sign-in/out actions, the `openapi-fetch` bearer middleware, and the full sign-out state teardown are greenfield per §8.1/§7, pinned by mock-based tests.

## Dependencies

- **New production dependencies (frontend, recorded here before use):** `@azure/msal-browser` and — per **D-P3-MSAL-LIB** (recommended) — `@azure/msal-react`. No other new production deps. Any addition is a plan amendment.
- **New build-time env (public identifiers, not secrets; documented in `app/.env.example`):** `VITE_MSAL_CLIENT_ID` (required for live sign-in), `VITE_MSAL_AUTHORITY` (default `https://login.microsoftonline.com/common`), `VITE_MSAL_REDIRECT_URI` (default `window.location.origin`), `VITE_API_SCOPE` (default `api://leafledger/ledger.write`). `.env.local` stays git-ignored; no secret is committed.
- **Test tooling:** the existing Vitest + Testing Library stack; MSAL is **mocked** (no live IdP, no Playwright/E2E in WP02 — live sign-in lands with the WP08 exit gate). Recorded so no E2E dependency is assumed.
- **Entra SPA app registration** (D-P3-MSAL-REG) — an external, user-provisioned config artifact, not a code dependency; implementation and tests do not require it.

## File list (implementation target — flat `app/src`, per approved D-P3-STRUCT)

**New — `app/src/application/auth/`**
- `msalConfig.ts` — env-driven MSAL `Configuration` (`sessionStorage`, `common`), scope constants (`api://leafledger/ledger.write`), `hasClientId()` guard.
- `msalInstance.ts` — the single `PublicClientApplication` (created from config), initialized once.
- `authTokens.ts` — `acquireApiToken()` (`acquireTokenSilent` + throttled `acquireTokenPopup` fallback) + the `openapi-fetch` bearer middleware registration attached to `apiClient`.
- `useAuth.ts` — `useAuth()` hook (account, `isSignedIn`, `signIn` = `loginPopup`, `signOut` = `logoutPopup`); `signOut` clears the TanStack Query cache + in-memory state.
- `README.md` — auth glue conventions (sessionStorage rationale, popup flow, "bearer attaches only at the generated client", "no client auto-join", the env contract).

**Modified — `app/src/app/`**
- `providers.tsx` (`AppRoot`) — mount the MSAL provider above `QueryClientProvider`/`RouterProvider`; add the `initialize()` gate (splash until initialized). Preserve the WP03 i18n slot.
- `AppLayout.tsx` — add the top-bar sign-in/out affordance (minimal chrome; placeholder copy; sign-in click is the popup's user gesture).
- (If needed) a small `AuthGate`/`SignInBar` component under `app/` for the signed-in/out shell chrome.

**Modified — other**
- `app/src/api/client.ts` — attach the bearer middleware from the application-layer auth module (hand-authored file; `schema.d.ts` untouched). Keep it a thin factory.
- `app/package.json` — add `@azure/msal-browser` (+ `@azure/msal-react` per D-P3-MSAL-LIB).
- `app/.env.example` (new) — document the four `VITE_MSAL_*`/`VITE_API_SCOPE` vars.
- `app/eslint.config.js` — only if the new `application/auth/` folder needs boundary-rule confirmation (rules stay green; no loosening).
- `docs/rebuild/plans/P3-WP02-msal-authentication.md` + `docs/rebuild/status.md` — notes/state.

**New — tests (`app/src/**` colocated)**
- MSAL config test (asserts `cacheLocation === 'sessionStorage'`, authority `common`, client id/authority/scope read from env, **no** `localStorage`).
- Provider/init-gate test (mocked MSAL: splash until `initialize()` resolves, then shell renders).
- Bearer-middleware test (signed-in mock → `Authorization: Bearer <token>` present on an outgoing request; signed-out / anonymous `getMeta()` → no header, request proceeds).
- Sign-in test (`signIn()` calls `loginPopup` with the API/login scopes).
- Sign-out teardown test (`signOut()` calls `logoutPopup` **and** empties the `QueryClient` cache; in-memory state cleared).
- Interactive-fallback test (`acquireTokenSilent` throws `InteractionRequiredAuthError` → one throttled `acquireTokenPopup`; gesture-less middleware path surfaces a single "interaction required" error, no loop).
- No-auto-join assertion (grep/string test: no `upsertMicrosoftUser`-style client membership/space provisioning).

No changes under `app/src/api/schema.d.ts` (generated), no backend files, no `backend/openapi/**` regeneration, no migration.

## Acceptance criteria (concrete, testable)

1. **`sessionStorage`, `common`, env-driven config.** A config test asserts the MSAL `Configuration` uses `cache.cacheLocation === 'sessionStorage'`, `authority === https://login.microsoftonline.com/common` (from env, default), and reads `clientId`/`redirectUri`/scope from `VITE_` env — with **no** hardcoded client id/secret in source and **no** `cacheLocation: 'localStorage'` anywhere (grep-proven).
2. **Provider mounts + initialization gate.** With a mocked MSAL instance, `AppRoot` renders a splash until `initialize()` resolves, then renders the shell; the MSAL provider sits **above** the router — proven by a render test. (Popup flow needs no pre-mount redirect processing.) WP01's existing shell tests stay green.
3. **Bearer attaches at the generated client (signed in).** With a mocked signed-in account and a stubbed `acquireTokenSilent` returning a token, an outgoing generated-client request carries `Authorization: Bearer <token>` — proven by a middleware test asserting the header on the request.
4. **Anonymous path still works (signed out).** With no account, the `getMeta()` call issued via `useMeta()` proceeds with **no** `Authorization` header and still resolves — proven by a test (the WP01 `getMeta` proof stays green with auth wired).
5. **Sign-in.** `signIn()` invokes `loginPopup` with the login/API scopes — proven by a mock assertion.
6. **Sign-out clears all app state.** `signOut()` invokes `logoutPopup` **and** empties the TanStack Query cache (`queryClient.clear()`) **and** drops in-memory auth state — proven by a test asserting the cache is empty and logout was called; combined with `sessionStorage`, no stale-space data survives.
7. **No client auto-join.** A grep/string test proves there is **no** `upsertMicrosoftUser`-style client-side membership/space auto-provisioning (the §2.5 stale-space class is not re-introduced).
8. **Interactive fallback, no loop.** A test drives `acquireTokenSilent` → `InteractionRequiredAuthError` → exactly one throttled `acquireTokenPopup` (or, in the gesture-less middleware path, a single surfaced "interaction required" error) — no tight popup/iframe loop.
9. **Configured-vs-not guard.** With `VITE_MSAL_CLIENT_ID` absent, `hasClientId()` is false and the shell shows a clear "sign-in not configured" state rather than crashing — proven by a test.
10. **Boundaries + budget green.** Auth glue lives in `application/auth/` + app-shell; no feature imports MSAL or handles a token; `npm run lint` (boundary rules intact) and `npm run check:page-budget` pass for every new file (all well under budget).
11. **Generated client + contract untouched.** `app/src/api/schema.d.ts` is byte-unchanged; no backend/OpenAPI change; the CI `contract` gate is unaffected.
12. **All gates green.** `npm run lint`, `npm run typecheck`, `npm test`, `npm run check:page-budget`, and the production build are green; `npm audit --omit=dev` shows **no new** vulnerabilities from `@azure/msal-*`.

## Boundary note

WP02 respects the frontend layering (**features → application → api**): the application-layer `auth/` module owns the MSAL instance, token acquisition, and the bearer middleware (attached to the generated client — the only data path, ADR-0003); the app-shell layer owns the provider mount, the init gate, and the sign-in/out chrome. **No feature** imports MSAL or acquires a token — features call the already-authenticated generated client. The two new production deps (`@azure/msal-browser`, `@azure/msal-react`) and the four `VITE_` env vars are recorded above before use. No secret enters the repo (client id + redirect URIs + API scope are public identifiers).

## Open questions / carry-forwards

- **D-P3-MSAL-REG** — the Entra SPA app registration (client id / redirect URIs / API-scope grant) is a user-provisioned config artifact. **Accepted route (2026-07-13):** no client id supplied → WP02 ships fully MSAL-mock-verified and the **live E2E sign-in journey is deferred to the WP08 exit gate**. The user may still supply the client id later to enable a live popup smoke before then.
- **D-P3-MSAL-FLOW = popup (chosen)** — interactive calls (`loginPopup`/`acquireTokenPopup`) require a user gesture; the gesture-less bearer middleware surfaces a throttled "interaction required" error instead of opening popups per request. Recorded so WP05–WP08 do not expect silent re-auth from a background request.
- **D-P3-MSAL-LIB / -BEARER / -SIGNOUT** — approved on the recommended routes (`@azure/msal-react`, `onRequest` middleware, full sign-out clear).
- **WP08 hub token** — WP08 (SignalR) needs an access token for the hub connection (the OLD hub read `?access_token=`); WP02 exposes `acquireApiToken()` for that. Recorded so WP08 reuses the seam rather than adding a second token path.
- **Correlation-id / OTel** — the WP01 error-log seam + a real OpenTelemetry SDK (§9) remain a later observability WP; WP02 adds no telemetry.
- **License entitlement UX** — real license gating is the Licensing module's concern; when it exists, the shell may surface a license-blocked state. Not WP02.

## Implementation log

## QA verdict

**FAIL — 2026-07-13, LL QA Reviewer**

1. **Sign-out does not guarantee MSAL cache teardown when the popup fails.** In `app/src/application/auth/useAuth.ts`, `signOut()` clears the active account and TanStack Query cache before calling `logoutPopup`, but a blocked or closed popup leaves the MSAL account cache intact. The hook then derives `account` from `accounts[0]`, so the user can immediately appear signed in again and stale identity state remains. Expected: sign-out must clear MSAL account/cache state even when the popup rejects, or otherwise prevent the cached account from restoring the signed-in state.
2. **Required acceptance coverage is missing.** The new auth tests contain only 4 tests in `msalConfig.test.ts` and `authTokens.test.ts`. There is no provider/initialization-gate render test, no `loginPopup` sign-in assertion, no `logoutPopup` + query-cache teardown/in-memory-state test, no configured shell-state test, and no explicit no-auto-join grep/string test. The passing 16-test suite therefore does not prove AC2, AC5, AC6, AC7, or AC9 as specified.

Passing reproduction: full frontend Vitest **16/16**; lint; typecheck; strict page budget; production build; `npm audit --omit=dev` (**0 vulnerabilities**); OpenAPI regeneration produced no `schema.d.ts` or `client.ts` drift. These passing gates do not close the two findings above.

**Required next action:** fix the sign-out failure path and add the missing focused acceptance tests, then rerun this review.

**PASS — 2026-07-13, LL QA Reviewer re-review:** Both findings are closed. `signOut()` now clears the MSAL cache after `logoutPopup()` regardless of popup failure and retains an explicit signed-out state so cached accounts cannot restore the shell. Added provider initialization, popup sign-in, failed-popup sign-out teardown, configured-state, and no-auto-join coverage. Full frontend Vitest **21/21**, lint, typecheck, strict page budget, production build, production audit (**0 vulnerabilities**), and OpenAPI artifact stability all pass. State → **done**; next action is LL Git commit/PR handling.

**FAIL — 2026-07-13, LL QA Reviewer follow-up review:** The prior implementation findings remain closed, but the acceptance evidence is still incomplete:

1. **AC8 throttle behavior is not proven.** `authTokens.test.ts` drives one `InteractionRequiredAuthError` and one `acquireTokenPopup` call, but never makes a second acquisition during the throttle window. The test therefore cannot prove that repeated gesture-less middleware requests surface the retryable interaction error without opening another popup.
2. **AC1 environment overrides are not proven.** `msalConfig.test.ts` asserts only default values and an empty client id. It does not verify `VITE_MSAL_CLIENT_ID`, `VITE_MSAL_AUTHORITY`, `VITE_MSAL_REDIRECT_URI`, or `VITE_API_SCOPE` overrides are reflected in the exported configuration and scope constants.
3. **AC4 generated-client anonymous path is not proven.** The bearer test invokes the exported middleware directly with a manually constructed `Request`; it does not issue the existing application `getMeta()` call through `apiClient` and assert that the request proceeds without an `Authorization` header. The direct middleware test proves mutation logic, but not the required generated-client integration path.

The executable gates remain green: frontend Vitest **21/21**, lint, typecheck, strict page budget, production build, production audit (**0 vulnerabilities**), and OpenAPI artifact stability. These do not close the three missing acceptance proofs.

**Required next action:** add the three focused tests above and rerun QA review.

**2026-07-14 — LL Frontend Dev:** Closed the three follow-up evidence gaps without changing production code. Extended `authTokens.test.ts` to issue the anonymous request through the shared generated client via `getMeta()` and assert no `Authorization` header; expanded the fallback test to make a second acquisition during the throttle window and assert no second popup; and added module-isolated `VITE_MSAL_CLIENT_ID`, `VITE_MSAL_AUTHORITY`, `VITE_MSAL_REDIRECT_URI`, and `VITE_API_SCOPE` override assertions in `msalConfig.test.ts`. Validation: focused auth **5/5**, full frontend Vitest **22/22**, lint, typecheck, strict page budget, production build, production audit (**0 vulnerabilities**), and OpenAPI artifact stability all pass. State → **verify**; next action is LL QA Reviewer re-review.

**PASS — 2026-07-14, LL QA Reviewer re-review:** The three follow-up findings are closed. AC8 now proves a second acquisition during the throttle window does not invoke a second popup; AC1 verifies all four Vite environment overrides; and AC4 exercises the anonymous metadata request through the shared generated client with no `Authorization` header. Scope remains within the approved WP02 file list; no feature imports MSAL or handles tokens; no client auto-join logic, backend, OpenAPI, or generated schema changes were introduced. Fresh validation passed: frontend Vitest **22/22**, lint, typecheck, strict page budget, production build, production audit (**0 vulnerabilities**), and OpenAPI artifact stability. State → **done**; next action is LL Git commit/PR handling.

- **2026-07-13 — LL Frontend Dev:** Implemented the MSAL `sessionStorage` configuration and `initialize()` gate, mounted `MsalProvider` above the query/router tree, added popup sign-in/sign-out with full TanStack Query teardown, and added the single `openapi-fetch` bearer middleware with silent acquisition plus throttled popup fallback. Added the configured-vs-not shell state, `.env.example`, auth conventions README, and mock-focused auth tests. No client auto-join logic, backend, OpenAPI, or generated schema changes. Validation: focused auth **4/4**, full frontend Vitest **16/16**, lint, typecheck, production build, strict page budget, and `npm audit --omit=dev` (0 vulnerabilities) all pass. State → **verify**; next action is LL QA Reviewer acceptance review.

- **2026-07-13 — LL Architect:** Plan approved by the user with **D-P3-MSAL-FLOW = popup** (overriding the recommended redirect); D-P3-MSAL-LIB/-BEARER/-SIGNOUT accepted on recommended routes; D-P3-MSAL-REG accepted mock-verified (no client id supplied → live E2E → WP08). Plan updated to popup semantics (init gate collapses to a plain `initialize()` gate; `signIn=loginPopup`, `signOut=logoutPopup`, fallback `acquireTokenPopup`; gesture-less middleware surfaces a throttled "interaction required" error). State `planned`, ready for LL Frontend Dev.
