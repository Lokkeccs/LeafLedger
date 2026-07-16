# P3-WP07 — Trial-balance report page (read-path proof: "post → see it")

- **Phase:** 3 (frontend re-platform) — the **read half** of the Phase-3 vertical slice's proof loop. WP06 delivered "post a balanced entry"; WP07 delivers "…and see it reflected in a server-computed report", closing the manual half of the Phase-3 exit journey (the live cross-browser half is WP08).
- **State:** done — **QA PASS 2026-07-15**. All five front-loaded decisions were approved by the user on their recommended routes (2026-07-14). D-P3-TB-PRESENTATION resolves to the **debit/credit split**; D-P3-TB-CURRENCY to **`VITE_DEMO_BASE_CURRENCY` (default CHF)**; D-P3-TB-INTEGRITY to **derive balanced from `totalBaseBalanceMinor === 0`** (no `/integrity` call); D-P3-TB-QUERY to the **application wrapper + hook on the reserved key**; D-P3-TB-SPACE to **reuse `VITE_DEMO_SPACE_ID`**.
- **Owner (implementation):** LL Frontend Dev. Single-agent, **frontend-only**. **No backend change** — the `GET …/reports/trial-balance` endpoint, its authorization (`ledger.read`), the `security_invoker` view, and the C1 presentation convention already exist and are merged (P2-WP07, PR #17).
- **Depends on:**
  - **P3-WP01** (done) — router (lazy child routes under `AppLayout`), TanStack Query provider + `qk` factory (**`qk.reports.trialBalance(spaceId)` already reserved and already invalidated by WP06's mutation**), error boundaries, desktop-first `AppLayout`.
  - **P3-WP04** (done) — shared primitives: `DataTable` (desktop-only, the report renders through it — no new table UI), `FormSection`, design tokens. Reuse before writing new UI.
  - **P3-WP03** (done) — i18n corpus + duplicate-key gate + the render-edge **`formatMoney(minorUnits, currency, locale)`** formatter (integer-minor-units → localized currency string; the money column formats only here).
  - **P3-WP05** (done) — `VITE_DEMO_SPACE_ID` demo-space seam (reused verbatim); the Development-only demo seed (CHF space + accounts + open period + dev Owner) makes a real posted entry visible end-to-end locally; the read-page precedent (`AccountsPage` → application wrapper + `useQuery` hook + `DataTable`) this WP mirrors exactly.
  - **P3-WP06** (done) — the posting flow whose `usePostJournalEntry` `onSuccess` **already invalidates `qk.reports.trialBalance(spaceId)`**, so a post → navigate → see-it loop refetches automatically. WP07 is the visible other end of that invalidation.
  - **P3-WP02** (done) — MSAL bearer wiring; the report query carries the bearer through the generated client (`ledger.read` is granted to every space role incl. Viewer).
  - **P2-WP07** (done, PR #17) — `GET /api/v1/spaces/{spaceId}/reports/trial-balance` returning `TrialBalanceReport { spaceId, lines: TrialBalanceRow[], totalBaseBalanceMinor }`; `TrialBalanceRow { accountId, accountCode, accountName, accountKind, baseBalanceMinor }`; raw signed **debit-positive** `baseBalanceMinor` (C1: `+` = net debit, `−` = net credit); whole-space total exactly `0` when balanced. **Already present in `app/src/api/schema.d.ts`.**
- **Blocks:** **P3-WP08** (SignalR live invalidation + Phase-3 exit journey — WP08's two-browser live-update demonstrates the same `reports.trialBalance` key refetching across clients; WP07 is the page that visibly updates).
- **Estimated size:** ≤ 1 day, single agent, well under the ceiling. One application-layer read wrapper + one query hook + one page (through `DataTable`) + route + nav + i18n + tests. Structurally identical to WP05b (accounts read page), minus the backend half.

## Context / scope note (LL Architect)

Part 4 §Phase 3 sets the Phase-3 exit criterion: *"post a balanced entry → see it in the trial-balance report; two browsers in one space live-update via a SignalR invalidation ping."* WP06 built the post; **WP07 builds the "see it in the trial-balance report"** — a signed-in user opens a trial-balance route and sees the server-computed, per-account net balances and the whole-space total over the demo space's posted entries, proving the report read path end-to-end (MSAL bearer → generated client → TanStack Query → `DataTable`). The SignalR/live-update half stays in **WP08**.

This is a **rewrite** WP per §5 (all data access Dexie → TanStack Query + generated client) — but the report itself is a **server-computed capability that did not exist in the old stack** (the OLD trial balance was UI-computed over Dexie; §72 lists "cross-entity SQL reporting (trial balance as a view)" as a *new* capability). The client does **no** aggregation, **no** balance computation, and **no** sign logic beyond a pure presentation split of the already-merged debit-positive value: it renders what the server view returns. There is therefore **no accounting behavior on the client**, **no golden fixtures, and no accounting consult** (see those sections).

The tracker row scopes this deliberately narrow: *"Minimal report page … proves 'post → see it'. Balance-sheet / P&L / account-drill / dashboard are Phase 4 (M1 Reports)."* WP07 renders **only** the trial balance. Balance-sheet, income-statement, integrity-hash display, period/date filtering, drill-down, and dashboards are **not** in this WP.

## Spec sources

- `docs/architecture/rebuild/03-target-architecture.md` §4.3 (Reporting: trial balance as an SQL view; `GET …/reports/trial-balance`), §7 (frontend structure — features → application → api; TanStack Query; app shell owns routing), §72 (server-computed reporting = a **new** capability, spec-derived rewrite not a port), §6 (`GET /api/v1/spaces/{spaceId}/reports/trial-balance`).
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 3 (the vertical slice + the **exit criterion** "post a balanced entry → see it in the trial-balance report; two browsers live-update"), §5 salvage/rewrite (all data access → TanStack Query + generated client; the OLD UI-computed reports are not ported).
- `docs/architecture/rebuild/06-feature-roadmap.md` M1 Reports (trial balance = the first report; balance-sheet / P&L / dashboards = later M1 / Phase 4).
- `docs/architecture/rebuild/07-vibe-coding-playbook.md` §107 (reuse shared primitives — the page renders through the WP04 `DataTable`).
- `.github/instructions/frontend.instructions.md` — layering **features → application → api**; page budget **450 lines / 30 imports / 20 state hooks**; **money is integer minor units, format only at the render edge (never float math on amounts)**; i18n every string in all locales; IDs are opaque strings; reuse shared primitives.
- `docs/rebuild/plans/P2-WP07-reporting-views-integrity.md` — the endpoint contract, the C1 debit-positive presentation convention, and the ≡ 0 total invariant the page reflects (does not re-compute).
- `docs/rebuild/plans/P3-WP05-accounts-read-endpoint-and-page.md` — the read-page pattern (`getAccounts`/`useAccounts`/`AccountsPage`/`DataTable`/`VITE_DEMO_SPACE_ID`/lazy route) this WP mirrors 1:1.
- `docs/rebuild/plans/P3-WP06-journal-entry-posting-flow.md` — the mutation that already invalidates `qk.reports.trialBalance`, closing the post→see-it loop.

## Backend contract (already merged — WP07 consumes it, no change)

`GET /api/v1/spaces/{spaceId}/reports/trial-balance` — `ledger.read` (every space role incl. Viewer); RLS-bound `security_invoker` view; read-only, no idempotency header (GET).

- **200** `TrialBalanceReport`: `{ spaceId (uuid), lines: TrialBalanceRow[], totalBaseBalanceMinor (int64) }`.
- **Row** `TrialBalanceRow`: `{ accountId (uuid), accountCode (int32), accountName (string), accountKind (string), baseBalanceMinor (int64, signed, debit-positive) }`.
- **Semantics (C1, merged):** `baseBalanceMinor` is the raw signed net over posted lines — `+` = net debit, `−` = net credit; the whole-space `totalBaseBalanceMinor` is exactly `0` when the ledger is balanced. Reversals net automatically (they are posted entries with negated base amounts).
- **Errors:** `401 auth.unauthenticated`, `403 auth.not_a_member` / `auth.permission_denied`, `space.not_found`.

The response carries **no per-row currency and no space base currency** — every amount is in the space's base currency (integer minor units). WP07 supplies the base currency for render-edge formatting from configuration (see **D-P3-TB-CURRENCY**); it never infers or computes a currency.

## Goal

A signed-in user navigates to a trial-balance route and sees the demo space's server-computed trial balance: one row per account (code, name, kind, and its net base-currency balance presented as debit/credit) plus the whole-space totals and a balanced indicator, over all posted entries — with the read path proven end-to-end and the post→see-it loop closed (post via WP06 → the invalidated `reports.trialBalance` key refetches → the new entry is visible). Concretely:

1. **Application layer:** `getTrialBalance(spaceId, client)` wraps `client.GET('/api/v1/spaces/{spaceId}/reports/trial-balance', …)` → a typed `TrialBalance` view model (rows + total); `useTrialBalance(spaceId)` = `useQuery({ queryKey: qk.reports.trialBalance(spaceId), queryFn })` (the key is already reserved and already invalidated by WP06).
2. **Feature UI:** a read-only `TrialBalancePage` rendering the rows through the WP04 `DataTable` with loading / empty / error(boundary) / rows states, a debit-column + credit-column presentation derived from the merged debit-positive sign (D-P3-TB-PRESENTATION), column + grand-total money formatted at the render edge via `formatMoney`, and a "balanced ✓ / unbalanced" indicator derived from `totalBaseBalanceMinor === 0` (D-P3-TB-INTEGRITY).
3. **Routing / shell:** a lazy `/reports/trial-balance` child route under `AppLayout` + a nav affordance.

WP07 adds **no** balance-sheet / P&L / integrity-hash / dashboard UI, **no** period/date filtering, **no** account drill-down, **no** export, and **no** backend/contract/OpenAPI/migration change.

## Scope

1. **Read wrapper — `app/src/application/reports.ts`:** `getTrialBalance(spaceId, client = apiClient)` calls `client.GET('/api/v1/spaces/{spaceId}/reports/trial-balance', { params: { path: { spaceId } } })`, throws a typed error when `data === undefined`, and maps to a frontend `TrialBalance` view type — `{ spaceId, rows: TrialBalanceRow[], totalBaseBalanceMinor: number }` where `TrialBalanceRow = { accountId, accountCode, accountName, accountKind, baseBalanceMinor }` (opaque string ids, integer minor units preserved — **no float math**). Single point of `src/api` access for reports (mirrors `getAccounts`/`getMeta`).
2. **Query hook — `app/src/application/query/useTrialBalance.ts`:** `useTrialBalance(spaceId)` = `useQuery({ queryKey: qk.reports.trialBalance(spaceId), queryFn: () => getTrialBalance(spaceId) })`. The key already exists in `qk` (`queryKeys.ts`) and is already the WP06 invalidation target — **reuse it, do not add a key**.
3. **Feature page — `app/src/features/reports/TrialBalancePage.tsx`:** read-only page resolving `VITE_DEMO_SPACE_ID` (D-P3-TB-SPACE), with:
   - loading state (`role="status"`), empty state (no rows), error surfaced to the route error boundary (throw on `isError`, matching `AccountsPage`),
   - a rows state rendering through `DataTable`: columns **code**, **name**, **kind**, **debit**, **credit** — where a row's `baseBalanceMinor > 0` shows its magnitude in the **debit** column and `< 0` shows its magnitude in the **credit** column (pure presentation of the merged debit-positive convention; `0` shows blank/dash in both), each money cell formatted via `formatMoney(magnitudeMinor, baseCurrency, locale)`,
   - a totals row / footer showing the summed debit and credit columns (equal when balanced) and the grand `totalBaseBalanceMinor`, plus a **balanced** indicator (`totalBaseBalanceMinor === 0` → "balanced", else "unbalanced" — BigInt/integer comparison, never float),
   - all copy as i18n keys; ≤ 450 lines (well within budget — this is a small page).
4. **Routing — `app/src/app/router.tsx`:** add a lazy `/reports/trial-balance` child route under `AppLayout` (matching the existing `accounts` / `journal-entries/new` lazy pattern).
5. **Shell nav — `app/src/app/AppLayout.tsx`:** add a `NavLink` to `/reports/trial-balance` (i18n `nav.*` key).
6. **i18n — `app/src/i18n/locales/en.json`, `de.json`:** all trial-balance page copy as keys in **both** locales (title/eyebrow/description, column headers, debit/credit, totals, balanced/unbalanced, loading/empty, nav label); duplicate-key gate green.
7. **Tests:** listed in acceptance criteria (wrapper mapping, hook wiring, page states, debit/credit presentation + totals/balanced, route resolution + error boundary, i18n presence).

**No** OpenAPI/TS regeneration is required — the contract already contains `TrialBalanceReport`/`TrialBalanceRow` and the `GetTrialBalance` operation (present in `app/src/api/schema.d.ts`). If any regeneration surfaces a diff, **stop** — that signals an unexpected backend change outside this WP.

## Non-goals (explicitly deferred)

- **No balance-sheet / income-statement / integrity-hash pages.** The `GET …/reports/balance-sheet`, `…/income-statement`, and `…/integrity` endpoints exist but their pages are M1/Phase 4 (roadmap Reports). WP07 renders only the trial balance.
- **No integrity-hash display / no `/integrity` call.** The balanced indicator is derived from `totalBaseBalanceMinor === 0` on the trial-balance response itself (D-P3-TB-INTEGRITY); the `/integrity` hash endpoint and its display are deferred.
- **No period / date-range filtering.** WP07 shows the whole-space current position (all posted entries), matching the WP07-backend whole-space snapshot. Period/date scoping is an M1/M2 follow-up (and a backend view-signature change).
- **No account drill-down / no per-account transaction list.** Clicking an account row does nothing at M1; the journal-lines drill is a later WP.
- **No export (CSV/PDF/print view).** Report export is Phase 4.
- **No real space picker / `GET /spaces`.** Reuse `VITE_DEMO_SPACE_ID` (D-P3-TB-SPACE), consistent with WP05/WP06.
- **No SignalR live update.** The page refetches on the invalidated `reports.trialBalance` key (the local half); cross-browser live invalidation is WP08.
- **No new backend endpoint / contract / migration / OpenAPI change.** Frontend-only; the contract already exists.
- **No journal-entries list / browse grid.** `qk.journalEntries.list` remains reserved but unused here; the trial balance (not a journal grid) is the "see it" surface.

## Decisions (front-loaded, non-accounting — all APPROVED by the user 2026-07-14 on their recommended routes)

Five decisions. None is an accounting decision (the server owns every rule; the client only presents already-merged values). All five were **approved on their recommended routes**; each is now an implementation constraint for LL Frontend Dev.

- **D-P3-TB-QUERY — the read convention. RECOMMEND: an application-layer read wrapper (`getTrialBalance`) + a `useTrialBalance` `useQuery` hook keyed on the existing `qk.reports.trialBalance(spaceId)`**, exactly mirroring `getAccounts`/`useAccounts` (WP05). Keeps `src/api` access in the application layer and reuses the key WP06 already invalidates.
  - *Alternative:* call the generated client in the component → breaks the features → application → api layering. Rejected.
- **D-P3-TB-PRESENTATION — how a row's signed balance is shown. RECOMMEND: split into a `debit` and a `credit` column** — `baseBalanceMinor > 0` → magnitude in **debit**, `< 0` → magnitude in **credit**, `0` → blank both. This is the recognizable trial-balance format and a **pure presentation of the already-merged debit-positive convention** (P2-WP07 C1: `+` = net debit); the column sums are equal iff the space is balanced, making "post → see it" legible. No new accounting behavior — it is the same sign convention the server already fixed, re-expressed in two columns.
  - *Alternative:* a single signed `balance` column (simplest, matches the raw contract field 1:1). Acceptable and even smaller, but less legible as a "trial balance" and less obviously demonstrates balancing. **If the user prefers the single-column form, it is equally correct** — record the choice here before implementation. Recommend the debit/credit split for legibility.
- **D-P3-TB-CURRENCY — the base currency for render-edge formatting. RECOMMEND: a configured `VITE_DEMO_BASE_CURRENCY` (default `CHF`, matching the WP05 demo seed)**, passed to `formatMoney(minorUnits, currency, locale)`. The trial-balance response carries integer minor units only (no per-row currency, no space currency), so a base-currency source is required to format; a configured demo value keeps WP07 a single read page without a spaces/settings endpoint. A real space-base-currency source (from `GET /spaces` or space settings) is a later WP, designed once alongside the real space picker.
  - *Alternative A:* format as a plain locale number to 2 decimals with no currency symbol → avoids the config but hard-codes 2 fraction digits (wrong for 0-/3-decimal currencies) and loses currency context. Rejected.
  - *Alternative B:* add a `baseCurrency` field to the backend report contract now → a backend/contract change outside this frontend-only WP. Rejected for WP07 (recorded as a carry-forward for the reports contract when the space picker lands).
- **D-P3-TB-INTEGRITY — the balanced indicator source. RECOMMEND: derive `balanced` from `totalBaseBalanceMinor === 0`** on the trial-balance response (integer comparison, no float, no extra call). The `/integrity` hash endpoint and its `balanced` flag exist but a second request and a hash display are unnecessary for the read-path proof.
  - *Alternative:* also call `GET …/integrity` and show the hash → a second query and UI for no M1 benefit; the trial-balance total already answers "balanced?". Rejected for WP07 (integrity display is a later ops/reports concern).
- **D-P3-TB-SPACE — space selection. RECOMMEND: reuse `VITE_DEMO_SPACE_ID`** (identical to WP05/WP06); a real `GET /spaces` + picker is a later WP. Keeps WP07 a single read page.
  - *Alternative:* build the space picker now → scope creep shared with WP05/WP06's deferral. Rejected (design once, later).

**Approval (2026-07-14):** the user approved all five decisions on their recommended routes with no overrides. D-P3-TB-PRESENTATION = debit/credit split; D-P3-TB-CURRENCY = `VITE_DEMO_BASE_CURRENCY` (default CHF); D-P3-TB-INTEGRITY = derive `balanced` from `totalBaseBalanceMinor === 0` (no `/integrity` call); D-P3-TB-QUERY = application wrapper + hook on the reserved `qk.reports.trialBalance` key; D-P3-TB-SPACE = reuse `VITE_DEMO_SPACE_ID`. The plan is unblocked.

## Accounting decisions

**None required — no LL Accounting Expert consult.** WP07 introduces **no** accounting behavior: the server computes the trial balance in a `security_invoker` SQL view (P2-WP07) and owns the sign convention (C1, merged). The client performs **no** aggregation and **no** balance/sign computation — it renders the server's per-account `baseBalanceMinor` and `totalBaseBalanceMinor` verbatim. The only client-side transform is the **debit/credit column split** (D-P3-TB-PRESENTATION), which is a pure re-expression of the **already-merged, QA-passed debit-positive convention** (P2-WP07 C1: `+` = net debit, `−` = net credit) — traceable, not invented. **If, during implementation, any presentation rule cannot be traced to that merged convention (e.g. a request to sign-flip specific account kinds, which is balance-sheet/P&L presentation — a different, Phase-4 report), stop and route to LL Accounting Expert** rather than inventing client sign logic. Two standing boundaries are respected, not decided: money stays integer minor units + ISO currency, formatted only at the render edge (no float math on amounts); the report is read-only (no mutation).

## Golden fixtures

**None required.** WP07 computes no financial value and ports no accounting function — it renders a server-computed report. Correctness is pinned by:
- **frontend component tests** — loading / empty / error / rows states; the debit/credit split for a positive, a negative, and a zero `baseBalanceMinor` row; the totals footer; the balanced (`total = 0`) vs unbalanced (`total ≠ 0`) indicator; money formatted via `formatMoney` at the render edge (integer minor units in, localized string out, no float),
- **wrapper/hook tests** — `getTrialBalance` maps the generated response to the view model; `useTrialBalance` uses `qk.reports.trialBalance(spaceId)`,
- **a router-level test** — `/reports/trial-balance` resolves lazily under `AppLayout` and a query failure renders the real `RouteErrorBoundary`.

Report *correctness* (the ≡ 0 invariant, the C1 signs, reversals netting) is already pinned **server-side** by the P2-WP07 integration tests and the P2-WP08 property suite; the client tests pin only the presentation. This mirrors the WP05/WP06 precedent (a read/UX surface is pinned by component/route tests, not golden fixtures). Recorded so QA does not expect a golden artifact.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e` (local checkout `C:\Programming\LeafLedger\Accounting`).

- **Reference only / non-port (§72 new capability):** the OLD trial balance was **UI-computed over Dexie/IndexedDB** inside React report components (no capturable pure oracle; the derived `baseCurrencyBalance` cache column was the derived-state drift the rewrite eliminates — see P2-WP07 Source material). §72 lists SQL-view reporting as a capability the old stack lacked. WP07 is therefore a **spec-derived rewrite**: a thin page over the server view, **not** a port. Only the abstract shape (a per-account list of net balances that sums to zero when balanced) informs the new page.
- **New capability (no OLD oracle to port):** the `getTrialBalance` read wrapper + `useTrialBalance` hook over the generated client — server-computed report consumption is new (the OLD app computed reports client-side over Dexie). Pinned by wrapper/hook/page tests, not a captured oracle.
- **Reuse (Phase 3):** `DataTable` + tokens (WP04); the `qk` factory (`reports.trialBalance` already reserved) + query conventions + error boundaries (WP01); the i18n corpus + duplicate-key gate + `formatMoney`/`formatDate` render-edge formatters (WP03); `VITE_DEMO_SPACE_ID` + the demo seed + the `AccountsPage` read-page pattern (WP05); MSAL bearer (WP02); the WP06 mutation invalidation that drives the refetch.

## Dependencies

- **No new production dependency.** Frontend uses existing React 19 / TanStack Query / `openapi-fetch` / react-i18next.
- **No new migration, no backend package, no OpenAPI/TS regeneration** — the endpoint and contract already exist and are merged (P2-WP07).
- **New config (not a secret):** `VITE_DEMO_BASE_CURRENCY` (default `CHF`) documented alongside the existing `VITE_DEMO_SPACE_ID` (D-P3-TB-CURRENCY). No `app/package.json` change.

## File list (implementation target)

**Application layer (new)**
- `app/src/application/reports.ts` — `getTrialBalance()`, `TrialBalance` / `TrialBalanceRow` view types.
- `app/src/application/query/useTrialBalance.ts` — `useTrialBalance(spaceId)` query hook on `qk.reports.trialBalance`.

**Feature UI (new)**
- `app/src/features/reports/TrialBalancePage.tsx` — read-only trial-balance page through `DataTable` (rows + totals + balanced indicator), ≤ 450 lines.

**Routing / shell (modified)**
- `app/src/app/router.tsx` — lazy `/reports/trial-balance` route.
- `app/src/app/AppLayout.tsx` — nav affordance.

**i18n (modified)**
- `app/src/i18n/locales/en.json`, `de.json` — trial-balance page + nav keys (both locales; duplicate-key gate green).

**Tests (new)**
- `app/src/application/reports.test.ts` — `getTrialBalance` maps the generated `TrialBalanceReport` → view model (rows + total; integer minor units preserved); throws on undefined data.
- `app/src/application/query/useTrialBalance.test.tsx` — the hook uses `qk.reports.trialBalance(spaceId)` and invokes `getTrialBalance`.
- `app/src/features/reports/TrialBalancePage.test.tsx` — loading, empty, rows (debit/credit split for positive/negative/zero rows), totals footer + balanced vs unbalanced indicator, money formatted via `formatMoney`, error thrown to boundary.
- Router-level coverage (extend `app/src/app/router.test.tsx`) — `/reports/trial-balance` resolves lazily under `AppLayout`; a query failure renders `RouteErrorBoundary`.

**No** `app/src/api/**` change (contract already present); **no** backend/OpenAPI/migration change.

## Acceptance criteria (concrete, testable)

1. **Read wrapper maps correctly.** `getTrialBalance(spaceId)` issues `GET /api/v1/spaces/{spaceId}/reports/trial-balance` and maps the `TrialBalanceReport` to a `TrialBalance` view model preserving every row's `accountId`/`accountCode`/`accountName`/`accountKind`/`baseBalanceMinor` (integer minor units, no float) and `totalBaseBalanceMinor`; a test asserts the mapping and that undefined data throws.
2. **Query hook wiring.** `useTrialBalance(spaceId)` uses `qk.reports.trialBalance(spaceId)` and calls `getTrialBalance`; a test asserts the query key and query function (mirroring `useAccounts.test.tsx`).
3. **Post→see-it loop closed (invalidation reuse).** The hook keys on the same `qk.reports.trialBalance(spaceId)` that `usePostJournalEntry` `onSuccess` already invalidates; a test (or an assertion referencing the WP06 invalidation test) confirms the key identity so a post triggers a trial-balance refetch. No new invalidation code is added here.
4. **Debit/credit presentation (D-P3-TB-PRESENTATION).** A row with `baseBalanceMinor > 0` renders its magnitude in the **debit** column (credit blank); `< 0` renders its magnitude in the **credit** column (debit blank); `0` renders blank/dash in both; proven by component tests over all three cases. (If D-P3-TB-PRESENTATION is overridden to a single signed column, this AC becomes: the signed balance renders in one column with correct sign.)
5. **Totals + balanced indicator (D-P3-TB-INTEGRITY).** The page shows summed debit and credit totals and the grand `totalBaseBalanceMinor`; a **balanced** indicator is shown when `totalBaseBalanceMinor === 0` and an **unbalanced** indicator otherwise (integer comparison, no float); tests cover a balanced (total 0, debit-sum == credit-sum) and an unbalanced (total ≠ 0) fixture.
6. **Money formatted at the render edge.** Every amount cell and total is formatted via `formatMoney(minorUnits, baseCurrency, locale)` with `baseCurrency` from `VITE_DEMO_BASE_CURRENCY` (default CHF, D-P3-TB-CURRENCY); no arithmetic on formatted strings and no float math on amounts; a test asserts a known minor-units value renders the expected localized string.
7. **Page states.** `TrialBalancePage` renders a loading state, an empty state (no rows), an error surfaced to the route error boundary (throw on `isError`, as `AccountsPage`), and a rows state through the WP04 `DataTable`; component tests cover all four; the page is ≤ 450 lines (page budget green).
8. **Routing + shell.** `/reports/trial-balance` resolves lazily under `AppLayout` with a shell nav affordance; a router-level test resolves the route and exercises the `RouteErrorBoundary` on a query failure.
9. **Layering + query conventions.** The page imports only `application` (`useTrialBalance`/`getTrialBalance`) + local + WP04 shared primitives; no `src/api`/`fetch` import in `features`; `useTrialBalance` uses the `qk` key; ESLint boundary/leaf gates green.
10. **i18n.** All trial-balance page + nav copy uses i18n keys present in **both** EN and DE; the duplicate-key build gate is green.
11. **No out-of-scope surface.** No balance-sheet/P&L/integrity/dashboard UI; no period/date filter; no drill-down; no export; no space picker; no backend/contract/OpenAPI/migration change (`app/src/api/**` unchanged; a regen would produce no diff); a scope scan confirms the diff is limited to the file list.
12. **All gates green.** Frontend: `npm run lint`, `npm run typecheck`, `npm test`, `npm run check:page-budget`, `npm run build`, duplicate-key check, and `npm audit --omit=dev` (no new vulnerabilities) green.

## Boundary note

WP07 is frontend-only and respects **features → application → api**: `TrialBalancePage` imports the `useTrialBalance`/`getTrialBalance` application layer (the sole `src/api` access point for reports) and the WP04 shared primitives; no feature file imports the generated client or calls `fetch`. Money stays integer minor units end-to-end and is formatted only at the render edge via `formatMoney`; the balanced check is an integer comparison. The server (SQL view + RLS second wall) remains the authority for every value; the client only presents.

## Split seam

**Not needed** — WP07 is ≤ 1 day and single-agent (one wrapper + one hook + one page + route/nav/i18n/tests). If the user prefers, an optional split would be WP07a (application: `getTrialBalance` + `useTrialBalance` + their tests) → WP07b (page + route + nav + i18n + component/route tests), but the WP is small enough to execute whole.

## Open questions / carry-forwards

- **Base-currency source (D-P3-TB-CURRENCY).** `VITE_DEMO_BASE_CURRENCY` is a slice-only stand-in; when the real space picker / `GET /spaces` (or space settings) lands, the page should take the space's base currency from server data, and the reports contract may gain a `baseCurrency` field (a backend/contract change, out of scope here). Recorded so it is designed once.
- **Real space picker / `GET /spaces` (later).** Shared deferral with WP05/WP06; `VITE_DEMO_SPACE_ID` is the stand-in.
- **Balance-sheet / income-statement / integrity pages (M1/Phase 4).** The endpoints exist (P2-WP07); their pages, plus period/date filtering, account drill-down, export, and dashboards, are later Reports WPs. WP07 deliberately renders only the trial balance.
- **Live invalidation (WP08).** WP07's `reports.trialBalance` query is the visible end of the N6 invalidation map; WP08 adds the SignalR cross-browser ping + coalescing so two browsers in one space live-update, completing the Phase-3 exit journey.
- **Presentation-form choice (D-P3-TB-PRESENTATION).** If the user prefers a single signed-balance column over the debit/credit split, record it here before implementation; the ACs adjust accordingly.

## Implementation log

### QA verdict

**PASS — 2026-07-15, LL QA Reviewer.** All 12 acceptance criteria are met within the declared WP07 scope. Focused WP07 tests pass **13/13** across wrapper mapping, hook wiring, debit/credit presentation, totals/status, loading/empty/error states, lazy route, and route-boundary behavior. Frontend lint, typecheck, strict page budget, i18n duplicate-key check, production build, `git diff --check`, and `npm audit --omit=dev` pass. The page budget reports `TrialBalancePage.tsx` at 42 lines, 5 imports, and 0 state hooks.

Scope, layering, security, financial-integrity, and patch-layering scans found no WP07 finding: no feature-level generated-client/fetch access, no write capability, no secrets, no backend/OpenAPI/migration changes, no generic catch/self-healing logic, and no out-of-scope report UI. Browser smoke reached `/reports/trial-balance` and observed the expected protected API 401 without a signed-in session.

Residual evidence note: the full frontend suite is **84/85** because the pre-existing `shell.test.tsx` expects `Sign-in not configured` while the current worktree resolves an MSAL client ID and renders `Sign in with Microsoft`; the failure reproduces in isolation and is outside the WP07 file list. This is unrelated worktree/test-environment debt, not a WP07 defect.

**WP07 → done.** Next action: LL Git handling.

- **LL Frontend Dev (2026-07-15):** Implemented the approved frontend-only slice: `getTrialBalance` + `useTrialBalance` on `qk.reports.trialBalance`, `TrialBalancePage` through `DataTable` with render-edge money formatting, debit/credit split, integer totals, and the server-total balanced indicator; lazy route, shell nav, `VITE_DEMO_BASE_CURRENCY`, EN/DE copy, and wrapper/hook/page/router coverage. Focused validation is **13/13**. Lint, typecheck, strict page budget, production build, duplicate-key check, and `npm audit --omit=dev` pass. Full Vitest is **84/85** because the existing shell test assumes an unconfigured MSAL environment while this workspace resolves a client ID; isolated reproduction confirms it is unrelated to WP07. State → **done**; merged to `main` via **PR #33** as commit `af16414`. Next action: P3-WP08 planning.

- **LL Architect (draft):** Plan drafted → state `proposed` → `planned` (awaiting user approval). Researched Part 3 §4.3/§6/§7/§72 (trial balance as a server SQL view = a new capability, spec-derived rewrite not a port; features → application → api; `GET …/reports/trial-balance`), Part 4 §Phase 3 + its **exit criterion** ("post a balanced entry → see it in the trial-balance report; two browsers live-update") + §5 (all data access = rewrite → TanStack Query + generated client), roadmap M1 Reports (trial balance first; balance-sheet/P&L/dashboards later), playbook §107, and the frontend instructions (page budget 450/30/20; money integer minor units formatted only at the render edge, no float; reuse shared primitives). Verified against merged code: the `GET …/reports/trial-balance` endpoint + `TrialBalanceReport`/`TrialBalanceRow` contract already exist in `app/src/api/schema.d.ts` (**no OpenAPI/TS regen**); `qk.reports.trialBalance(spaceId)` is already reserved **and already invalidated by WP06's `usePostJournalEntry` `onSuccess`** (the post→see-it loop is pre-wired — WP07 is the visible other end); `AccountsPage`/`getAccounts`/`useAccounts` set the exact read-page precedent to mirror; the WP04 `DataTable` + WP03 `formatMoney(minorUnits, currency, locale)` render-edge formatter + `VITE_DEMO_SPACE_ID` demo seam are reused; the response carries **no currency** so a configured base currency is needed to format (D-P3-TB-CURRENCY). Inspected the OLD reporting: UI-computed over Dexie with no capturable oracle (§72 new capability) → confirmed **full rewrite, no port**; the client does no aggregation/sign computation beyond a pure debit/credit split of the merged debit-positive value — **no golden fixtures, no accounting consult** (server-authoritative; the only transform traces to the merged P2-WP07 C1 convention; if any presentation rule can't be traced to it, stop and route to LL Accounting Expert). Five front-loaded non-accounting decisions for sign-off: **D-P3-TB-QUERY** (application wrapper + hook on the reserved key), **D-P3-TB-PRESENTATION** (debit/credit split vs single signed column), **D-P3-TB-CURRENCY** (`VITE_DEMO_BASE_CURRENCY` default CHF vs plain number vs backend contract change), **D-P3-TB-INTEGRITY** (derive balanced from `total === 0` vs call `/integrity`), **D-P3-TB-SPACE** (reuse `VITE_DEMO_SPACE_ID`). 12 concrete ACs; no split needed (≤ 1 day). Implementation blocked until the decisions are approved.

- **LL Architect (approval):** User approved all five decisions (**D-P3-TB-QUERY**, **D-P3-TB-PRESENTATION**, **D-P3-TB-CURRENCY**, **D-P3-TB-INTEGRITY**, **D-P3-TB-SPACE**) on their recommended routes (2026-07-14). No overrides — debit/credit split, `VITE_DEMO_BASE_CURRENCY` (default CHF), balanced derived from `totalBaseBalanceMinor === 0` (no `/integrity` call), application wrapper + hook on the reserved `qk.reports.trialBalance` key, and reuse of `VITE_DEMO_SPACE_ID`. Decisions are now implementation constraints; the plan is unblocked. State stays `planned`, ready for LL Frontend Dev.
