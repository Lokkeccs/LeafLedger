# P4-WP02 — Balance-sheet & income-statement report pages (M1 Reports triad completion)

- **Phase:** 4 (feature porting), **Stage A** (complete M1 — launch scope). An early Phase-4 feature WP (the first follows the P4-WP01 design-system foundation): completes the M1 Reports **core-statements triad** — P3-WP07 delivered the trial balance; this WP adds the balance sheet and the income statement (P&L), the two primary OR 959/959a/959b statements a business/personal space needs day-to-day.
- **State:** done — QA PASS; all acceptance-test evidence is present and PR #37 is merged to `main`. The six front-loaded, **non-accounting** decisions are approved (see Decisions). No accounting consult or golden fixtures required.
- **Owner (implementation):** LL Frontend Dev. Single-agent, **frontend-only**. **No backend change** — the two endpoints, their `ledger.read` authorization, the `security_invoker` presentation views (with the C1 sign convention already applied server-side), and the `BalanceSheetReport` / `IncomeStatementReport` / `ReportLine` contracts already exist and are merged (P2-WP07, PR #17) and are present in `app/src/api/schema.d.ts`.
- **Estimated size:** ≤ 2 days, single agent. Two read wrappers + two query hooks + two pages (through `DataTable`) + two routes + nav + i18n + a small invalidation extension + tests. Each page is structurally identical to the P3-WP07 `TrialBalancePage`; together they stay well within the ≤2-day ceiling.
- **Depends on:**
  - **P2-WP07** (done, PR #17) — `GET /api/v1/spaces/{spaceId}/reports/balance-sheet` → `BalanceSheetReport { spaceId, lines: ReportLine[], currentResultMinor }`; `GET …/reports/income-statement` → `IncomeStatementReport { spaceId, lines: ReportLine[], netResultMinor }`; `ReportLine { accountId (uuid|null), accountCode (int32|null), name, accountKind, amountMinor (int64), isDerived (bool) }`. **The C1 presentation sign convention is applied inside the SQL views** (balance sheet keeps Assets as-is and flips Liabilities/Equity; income statement flips Income and keeps Expenses positive; the derived `net_result` / current-result lines carry `isDerived: true`). Already in `schema.d.ts`.
  - **P3-WP07** (done, PR #33) — the trial-balance read page this WP mirrors 1:1 (`getTrialBalance` / `useTrialBalance` / `TrialBalancePage` / `DataTable` / `VITE_DEMO_SPACE_ID` / `VITE_DEMO_BASE_CURRENCY` / lazy route + nav / render-edge `formatMoney`).
  - **P3-WP04** (done) — shared primitives: `DataTable` (desktop-only; the reports render through it — no new table UI), `FormSection`, design tokens.
  - **P3-WP03** (done) — i18n corpus + duplicate-key build gate + the render-edge `formatMoney(minorUnits, currency, locale)` formatter (money formats only here; never float math on amounts).
  - **P3-WP06** (done) — the posting flow whose `usePostJournalEntry` `onSuccess` invalidation set this WP extends so a post refreshes the two new reports (see D-P4-RPT-INVALIDATION).
  - **P3-WP08** (done) — the SignalR N6 client `invalidationMap`; this WP maps the existing server-emitted `reports.trialBalance` topic to also invalidate the two new report keys, so live cross-browser update works **without a backend/SignalR change** (see D-P4-RPT-INVALIDATION).
  - **P3-WP02** (done) — MSAL bearer wiring; the report queries carry the bearer through the generated client (`ledger.read` is granted to every space role incl. Viewer).
  - **P4-WP01** (design-system foundation) — **soft dependency.** These pages SHOULD render against the P4-WP01 design tokens / component-styling guidelines; per the approved soft-dependency sequencing they may proceed in parallel and inherit the tokens automatically (they render only through the P3-WP04 token-driven primitives, so a later token change restyles them for free). No hard block.
- **Blocks:** nothing hard. P4-WP03 (account drill-down) and P4-WP04 (dashboard) build on the same report-read pattern but do not depend on this WP's files.

## Context / scope note (LL Architect)

The [feature roadmap](../architecture/rebuild/06-feature-roadmap.md) M1 Reports row lists *"Trial balance, balance sheet, P&L, account detail/drill, overview dashboard (server-computed)"*. P3-WP07 shipped the trial balance and explicitly deferred *"Balance-sheet / P&L / account-drill / dashboard"* to Phase 4. **This WP is the balance-sheet + P&L half of that deferral** — the two remaining primary statements. Account detail/drill (P4-WP03) and the dashboard (P4-WP04) are separate, later WPs (each needs a new backend read view).

Like P3-WP07, this is a **spec-derived rewrite** per Part 4 §5 (all data access Dexie → TanStack Query + generated client), and the reports themselves are **server-computed capabilities the old stack lacked** (§72 — the OLD balance sheet / P&L were UI-computed over Dexie, no capturable pure oracle; the derived cached balance columns were exactly the derived-state drift the SQL-view approach eliminates). The client performs **no** aggregation and **no** sign computation: the `security_invoker` presentation views already applied the C1 sign convention server-side (resolved by LL Accounting Expert, merged in P2-WP07), so each page renders `ReportLine.amountMinor` **verbatim**. There is therefore **no accounting behavior on the client, no golden fixtures, and no accounting consult** (see those sections).

## Spec sources

- `docs/architecture/rebuild/03-target-architecture.md` §4.3 (Reporting: balance sheet / income statement as SQL views), §7 (frontend structure — features → application → api; TanStack Query; app shell owns routing), §72 (server-computed reporting = a **new** capability, spec-derived rewrite not a port), §6 (the two report endpoints).
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 4 (feature porting; reports/dashboards land as SQL views — "kills derived-summary drift"), §5 (salvage/rewrite: all data access → TanStack Query + generated client; the OLD UI-computed reports are not ported).
- `docs/architecture/rebuild/06-feature-roadmap.md` M1 Reports (trial balance, **balance sheet, P&L**, account detail/drill, dashboard) — this WP is the BS + P&L entries.
- `docs/architecture/rebuild/07-vibe-coding-playbook.md` §107 (reuse shared primitives — the pages render through the WP04 `DataTable`).
- `.github/instructions/frontend.instructions.md` — layering **features → application → api**; page budget **450 lines / 30 imports / 20 state hooks**; **money is integer minor units, formatted only at the render edge (never float math on amounts)**; i18n every string in all locales; IDs are opaque strings; reuse shared primitives.
- `docs/rebuild/plans/P2-WP07-reporting-views-integrity.md` — the two endpoint contracts + the **C1 balance-sheet / income-statement presentation convention** (server-side sign flips + the derived net-result / current-result line) that these pages render (do not re-compute).
- `docs/rebuild/plans/P3-WP07-trial-balance-report-page.md` — the read-page pattern (application wrapper + `useQuery` hook on a reserved key + page through `DataTable` + lazy route + `VITE_DEMO_*` seams + render-edge `formatMoney`) this WP mirrors exactly, plus the standing guard that any *new* client sign logic must stop and route to LL Accounting Expert.

## Backend contract (already merged — this WP consumes it, no change)

Both routes: `ledger.read` (every space role incl. Viewer); RLS-bound `security_invoker` view; read-only, no idempotency header (GET). Errors: `401 auth.unauthenticated`, `403 auth.not_a_member` / `auth.permission_denied`, `space.not_found`.

- **`GET /api/v1/spaces/{spaceId}/reports/balance-sheet`** → **200** `BalanceSheetReport`:
  - `{ spaceId (uuid), lines: ReportLine[], currentResultMinor (int64) }`.
- **`GET /api/v1/spaces/{spaceId}/reports/income-statement`** → **200** `IncomeStatementReport`:
  - `{ spaceId (uuid), lines: ReportLine[], netResultMinor (int64) }`.
- **`ReportLine`**: `{ accountId (uuid | null), accountCode (int32 | null), name (string), accountKind (string), amountMinor (int64), isDerived (bool) }`.
  - **Semantics (C1, merged, server-side):** `amountMinor` is the **presentation** amount — the SQL view has already applied the sign convention (balance sheet: Assets positive, Liabilities/Equity flipped; income statement: Income flipped positive, Expenses positive). `isDerived: true` marks the computed subtotal/total lines (`net_result` on the income statement; the current-result line surfaced once in equity on the balance sheet) — these have `accountId`/`accountCode` `null`. `currentResultMinor` / `netResultMinor` are the headline totals.

The responses carry **no per-line currency and no space base currency** — every amount is in the space's base currency (integer minor units). This WP supplies the base currency for render-edge formatting from configuration (D-P4-RPT-CURRENCY), identical to P3-WP07; it never infers or computes a currency.

## Goal

A signed-in user navigates to a `/reports/balance-sheet` route and an `/reports/income-statement` route and sees the demo space's server-computed statements — the account lines (code, name, kind, amount) grouped by kind, the derived subtotal/total lines rendered distinctly, and the headline result — over all posted entries, with the read path proven end-to-end (MSAL bearer → generated client → TanStack Query → `DataTable`) and the post→see-it loop closed (post via WP06 → the newly-invalidated report keys refetch). Concretely:

1. **Application layer:** `getBalanceSheet(spaceId, client)` and `getIncomeStatement(spaceId, client)` wrap the two `client.GET(...)` calls → typed `BalanceSheet` / `IncomeStatement` view models; `useBalanceSheet(spaceId)` / `useIncomeStatement(spaceId)` = `useQuery` on new reserved keys `qk.reports.balanceSheet(spaceId)` / `qk.reports.incomeStatement(spaceId)`.
2. **Feature UI:** read-only `BalanceSheetPage` and `IncomeStatementPage` rendering the lines through the WP04 `DataTable` with loading / empty / error(boundary) / rows states, sectioned by `accountKind`, the `isDerived` lines styled as subtotal/total rows, the headline result shown, all money formatted at the render edge via `formatMoney`.
3. **Routing / shell:** lazy `/reports/balance-sheet` and `/reports/income-statement` child routes under `AppLayout` + nav affordances.
4. **Invalidation:** extend `usePostJournalEntry` and the N6 `invalidationMap` so a post (local) and a `reports.trialBalance` SignalR ping (cross-browser) refresh the two new reports — **no backend/SignalR change** (D-P4-RPT-INVALIDATION).

This WP adds **no** account drill-down, **no** dashboard, **no** integrity-hash display, **no** period/date filtering, **no** export, and **no** backend/contract/OpenAPI/migration change.

## Scope

1. **Read wrappers — `app/src/application/reports.ts`** (extend the existing file that holds `getTrialBalance`): add `getBalanceSheet(spaceId, client = apiClient)` and `getIncomeStatement(spaceId, client = apiClient)`, each calling the corresponding `client.GET('/api/v1/spaces/{spaceId}/reports/…', { params: { path: { spaceId } } })`, throwing a typed error when `data === undefined`, and mapping to frontend view types:
   - `BalanceSheet = { spaceId, lines: ReportLine[], currentResultMinor: number }`,
   - `IncomeStatement = { spaceId, lines: ReportLine[], netResultMinor: number }`,
   - `ReportLine = { accountId: string | null, accountCode: number | null, name: string, accountKind: string, amountMinor: number, isDerived: boolean }` (opaque string ids, integer minor units preserved — **no float math**). Single point of `src/api` access for reports (mirrors `getTrialBalance`).
2. **Query hooks — `app/src/application/query/useBalanceSheet.ts`, `useIncomeStatement.ts`:** `useQuery({ queryKey: qk.reports.balanceSheet(spaceId), queryFn: () => getBalanceSheet(spaceId) })` and the income-statement analogue.
3. **Query keys — `app/src/application/query/queryKeys.ts`:** add `balanceSheet: (spaceId) => ['reports','balanceSheet',spaceId]` and `incomeStatement: (spaceId) => ['reports','incomeStatement',spaceId]` alongside the existing `trialBalance` key. Update `queryKeys.test.ts` and `application/query/README.md`.
4. **Feature pages — `app/src/features/reports/BalanceSheetPage.tsx`, `IncomeStatementPage.tsx`:** read-only pages resolving `VITE_DEMO_SPACE_ID` (D-P4-RPT-SPACE), with:
   - loading state (`role="status"`), empty state (no lines), error surfaced to the route error boundary (throw on `isError`, matching `TrialBalancePage`),
   - a rows state through `DataTable`: columns **code**, **name**, **kind**, **amount** — `amountMinor` rendered **verbatim** via `formatMoney(amountMinor, baseCurrency, locale)` (the server already applied C1 signs; the client applies **none**); lines **grouped/sectioned by `accountKind`**; `isDerived` lines rendered as distinct subtotal/total rows (D-P4-RPT-PRESENTATION),
   - the headline result (`currentResultMinor` / `netResultMinor`) shown in a footer/total row, formatted via `formatMoney`,
   - all copy as i18n keys; each page ≤ 450 lines (well within budget).
5. **Routing — `app/src/app/router.tsx`:** add lazy `/reports/balance-sheet` and `/reports/income-statement` child routes under `AppLayout` (matching the existing `reports/trial-balance` lazy pattern). Update `router.test.tsx`.
6. **Shell nav — `app/src/app/AppLayout.tsx`:** add `NavLink`s to the two routes (i18n `nav.*` keys). Update `shell.test.tsx` if it asserts nav content.
7. **Invalidation — `app/src/application/query/usePostJournalEntry.ts` + `invalidationMap.ts`:** per D-P4-RPT-INVALIDATION, (a) add `qk.reports.balanceSheet(spaceId)` and `qk.reports.incomeStatement(spaceId)` to the post-success `invalidateQueries` set; (b) map the existing `'reports.trialBalance'` topic in `invalidationMap` to also emit the two new report keys (or add the keys under that topic's returned array). Update `invalidationMap.test.ts` and `usePostJournalEntry.test.tsx`.
8. **i18n — `app/src/i18n/locales/en.json`, `de.json`:** all balance-sheet + income-statement page copy as keys in **both** locales (titles/eyebrows/descriptions, column headers, section headers per account kind, derived/subtotal/total labels, headline result labels, loading/empty, nav labels); duplicate-key gate green.
9. **Tests:** listed in acceptance criteria.

**No** OpenAPI/TS regeneration is required — `schema.d.ts` already contains `BalanceSheetReport` / `IncomeStatementReport` / `ReportLine` and the `GetBalanceSheet` / `GetIncomeStatement` operations. If any regeneration surfaces a diff, **stop** — that signals an unexpected backend change outside this WP.

## Non-goals (explicitly deferred)

- **No account detail / drill-down page** — clicking a line does nothing here; the per-account posted-lines drill is **P4-WP03** (needs a new backend read view).
- **No overview dashboard** — **P4-WP04**.
- **No integrity-hash display / no `/integrity` call** — a balance sheet / P&L do not surface the trial-balance hash.
- **No period / date-range filtering** — whole-space current position, matching the P2-WP07 whole-space views. Period/date scoping is a later WP (a backend view-signature change).
- **No export (CSV / PDF / print view)** — report export is a later Phase-4 WP.
- **No real space picker / `GET /spaces`** — reuse `VITE_DEMO_SPACE_ID` (D-P4-RPT-SPACE), consistent with P3-WP05/06/07.
- **No new server SignalR topic** — the existing `reports.trialBalance` ping is mapped client-side to the new report keys (D-P4-RPT-INVALIDATION).
- **No new backend endpoint / contract / migration / OpenAPI change** — frontend-only; both contracts already exist.

## Decisions (front-loaded, non-accounting — awaiting user sign-off)

Six decisions. **None is an accounting decision** — the server owns every rule; the client only presents already-merged values. Each recommended route is the smallest-diff continuation of the merged P3-WP07 precedent.

- **D-P4-RPT-SCOPE — one WP or two.** **RECOMMEND: one WP covering both the balance-sheet and income-statement pages.** They are trivial, structurally identical presentations of already-merged views; together they stay ≤ 2 days and each is independently test-verified. *Alternative:* two WPs (BS, then P&L) — acceptable but slices too thin for near-duplicate work. Recommend one WP.
- **D-P4-RPT-QUERY — the read convention.** **RECOMMEND: application-layer read wrappers (`getBalanceSheet` / `getIncomeStatement`) + `useBalanceSheet` / `useIncomeStatement` hooks keyed on new reserved `qk.reports.balanceSheet` / `qk.reports.incomeStatement` keys**, exactly mirroring `getTrialBalance` / `useTrialBalance`. Keeps `src/api` access in the application layer. *Alternative:* call the generated client in the component → breaks features → application → api layering. Rejected.
- **D-P4-RPT-PRESENTATION — how a `ReportLine` is rendered.** **RECOMMEND: render `amountMinor` verbatim in a single amount column (the server already applied C1 signs), section the account lines by `accountKind`, and render `isDerived` lines as distinct subtotal/total rows; show the headline `currentResultMinor` / `netResultMinor` in a footer.** No client sign logic whatsoever. *Alternative A:* re-derive signs client-side per account kind → **forbidden** (that is accounting behavior; the server already owns it — would trigger the P3-WP07 stop-and-consult guard). Rejected. *Alternative B:* one flat list with no kind sectioning → less legible as a statement; the `accountKind` field is already present for exactly this grouping. Recommend sectioning.
- **D-P4-RPT-INVALIDATION — closing the post→see-it loop and live-update without a backend change.** **RECOMMEND: (a) extend `usePostJournalEntry`'s success invalidation to also invalidate the two new report keys (local loop); (b) map the existing server-emitted `reports.trialBalance` SignalR topic in the client N6 `invalidationMap` to also invalidate the two new report keys (cross-browser).** The server already pings `reports.trialBalance` after every post — semantically "the reports changed" — so no new server topic is needed. *Alternative:* add new server SignalR topics (`reports.balanceSheet`, `reports.incomeStatement`) + emit them post-commit → a backend/SignalR change outside this frontend-only WP; unnecessary because a post that changes the trial balance changes these two reports identically. Rejected for WP01 (recorded as a carry-forward if per-report ping granularity is ever wanted).
- **D-P4-RPT-CURRENCY — the base currency for render-edge formatting.** **RECOMMEND: reuse the configured `VITE_DEMO_BASE_CURRENCY` (default `CHF`)**, passed to `formatMoney(minorUnits, currency, locale)` — identical to P3-WP07. The responses carry integer minor units only. A real space-base-currency source (from `GET /spaces` / space settings) is a later WP designed once alongside the real space picker. *Alternatives* (plain locale number; add a `baseCurrency` backend field) rejected for the same reasons as P3-WP07 D-P3-TB-CURRENCY.
- **D-P4-RPT-SPACE — space selection.** **RECOMMEND: reuse `VITE_DEMO_SPACE_ID`** (identical to P3-WP05/06/07); a real `GET /spaces` + picker is a later WP.

## Accounting decisions

**None required — no LL Accounting Expert consult.** This WP introduces **no** accounting behavior: the balance-sheet and income-statement presentation sign convention (C1 — Assets as-is, Liabilities/Equity flipped, Income flipped positive, Expenses positive, one derived net/current-result line surfaced once in equity without double-counting) was resolved by LL Accounting Expert and **merged into the `security_invoker` SQL views** in P2-WP07. The client renders `ReportLine.amountMinor` **verbatim** and performs **no** aggregation and **no** sign computation.

**Standing guard (from P3-WP07, load-bearing here):** if during implementation any presentation rule cannot be traced to the already-merged server C1 convention — in particular any request to sign-flip, re-group, or subtotal account kinds in a way the SQL view does not already encode — **stop and route to LL Accounting Expert** rather than inventing client-side accounting logic. Grouping lines by the server-provided `accountKind` and rendering server-provided `isDerived` subtotal lines is presentation only (it re-orders, it does not compute); introducing a *new* subtotal the server does not emit would cross into accounting and must stop. Two standing boundaries are respected, not decided: money stays integer minor units + ISO currency, formatted only at the render edge (no float math on amounts); the reports are read-only (no mutation).

## Golden fixtures

**None required.** This WP computes no financial value and ports no accounting function — it renders two server-computed reports. Report *correctness* (the C1 signs, the derived net/current-result lines, reversals netting) is already pinned **server-side** by the P2-WP07 integration tests (which seed all five account kinds + net/current result + unbalanced cases) and the P2-WP08 property suite. This WP's correctness is pinned by **frontend component / hook / route tests** (see acceptance criteria), mirroring the P3-WP05/06/07 precedent (a read/UX surface is pinned by component/route tests, not golden fixtures). Recorded so QA does not expect a golden artifact.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e` (local checkout `C:\Programming\LeafLedger\Accounting`).

- **Reference only / non-port (§72 new capability):** the OLD balance sheet and P&L were **UI-computed over Dexie/IndexedDB** inside React report components (no capturable pure oracle; the derived cached balance columns were the derived-state drift the SQL-view approach eliminates — see P2-WP07 Source material). This WP is a **spec-derived rewrite** — thin pages over the server views, **not** a port. Only the abstract shape (kind-grouped account lines + subtotals + a headline result) informs the new pages.
- **Salvage (pattern, not code):** the P3-WP07 `TrialBalancePage` / `getTrialBalance` / `useTrialBalance` structure is the template these pages copy.

## Acceptance criteria (concrete tests — all must pass)

1. **`getBalanceSheet` mapping** — `app/src/application/reports.test.ts`: given a mocked client returning a `BalanceSheetReport` (account lines of each kind + a derived current-result line + `currentResultMinor`), `getBalanceSheet` returns the `BalanceSheet` view model with ids as opaque strings, `amountMinor` as integers (no float), and `isDerived` preserved; throws when `data === undefined`.
2. **`getIncomeStatement` mapping** — same file: analogous for `IncomeStatementReport` (income + expense lines + derived `net_result` line + `netResultMinor`).
3. **Hooks wire the reserved keys** — `useBalanceSheet.test.tsx` / `useIncomeStatement.test.tsx`: each `useQuery` uses `qk.reports.balanceSheet(spaceId)` / `qk.reports.incomeStatement(spaceId)` and calls its wrapper.
4. **Query-key factory** — `queryKeys.test.ts`: `qk.reports.balanceSheet('sp_1')` ⇒ `['reports','balanceSheet','sp_1']`; `qk.reports.incomeStatement('sp_1')` ⇒ `['reports','incomeStatement','sp_1']`.
5. **BalanceSheetPage states** — `BalanceSheetPage.test.tsx`: loading (`role="status"`), empty (no lines), and rows states render; account lines are grouped by `accountKind`; a positive, a flipped-sign (already server-signed), and a derived line each render their `amountMinor` **verbatim** through `formatMoney` (integer minor units in, localized string out, no float); the derived current-result line renders as a distinct subtotal/total row; `currentResultMinor` renders in the footer; a query error throws to the route error boundary.
6. **IncomeStatementPage states** — `IncomeStatementPage.test.tsx`: analogous, incl. the derived `net_result` line and `netResultMinor` footer.
7. **No client sign logic** — a page test asserts that a `ReportLine` with a given `amountMinor` renders that exact magnitude/sign (the component does not negate, abs, or re-derive) — pinning the "render verbatim" contract.
8. **Routing** — `router.test.tsx`: `/reports/balance-sheet` and `/reports/income-statement` resolve lazily under `AppLayout`; a query failure on each renders the real `RouteErrorBoundary`.
9. **Post→see-it invalidation** — `usePostJournalEntry.test.tsx`: a successful post calls `invalidateQueries` with `qk.reports.balanceSheet(spaceId)` and `qk.reports.incomeStatement(spaceId)` (in addition to the existing trial-balance + journal-list keys).
10. **Live-update mapping** — `invalidationMap.test.ts`: the `reports.trialBalance` topic yields (among its keys) `['reports','balanceSheet',spaceId]` and `['reports','incomeStatement',spaceId]`, so a server ping refreshes the two pages cross-browser with no backend change.
11. **i18n presence** — every new balance-sheet / income-statement / nav key exists in **both** `en.json` and `de.json`; the duplicate-key build gate is green.
12. **Gates green + no contract drift** — full frontend Vitest green; lint, typecheck, strict page budget (both pages ≤ 450 lines / 30 imports / 20 state hooks), production build, duplicate-key check, and `npm audit --omit=dev` (0 vulnerabilities) pass; **`schema.d.ts` and `backend/openapi/leafledger-v1.json` are byte-unchanged** (regeneration produces no diff — no backend touched).

## Open questions

- None blocking. Carry-forwards (design once, later): a real space-base-currency source + space picker (shared with P3-WP05/06/07's `VITE_DEMO_*` deferral); optional per-report SignalR ping granularity (D-P4-RPT-INVALIDATION alternative); report export and period/date filtering (later Phase-4 WPs).

## Definition of done

All 12 acceptance criteria pass; state → `verify`; LL QA Reviewer acceptance review; then LL Git. No backend / OpenAPI / migration change; no golden fixtures; no accounting consult; layering (features → application → api) and page budgets intact.

## Implementation log

- **2026-07-17 — LL Frontend Dev:** Implemented application wrappers, query hooks/keys, statement pages through `DataTable`, server-kind grouping, derived-row styling, render-edge money formatting, lazy routes, shell navigation, EN/DE copy, local and SignalR invalidation, and focused coverage. Frontend gates pass: focused WP02 tests **26/26**, full Vitest **117/117**, typecheck, lint, strict page budget, i18n/design-token checks, production build, and `npm audit --omit=dev` (**0 vulnerabilities**). `app/src/api/schema.d.ts` and `backend/openapi/leafledger-v1.json` are unchanged. Next: LL QA Reviewer acceptance review.

## QA verdict

**PASS — 2026-07-18, LL QA Reviewer**

The AC8 finding was remediated by adding route-level failure tests for both `/reports/balance-sheet` and `/reports/income-statement`, asserting the real `RouteErrorBoundary` renders and the underlying error is not exposed. Focused router coverage is **10/10**.

Independent QA gates: full frontend Vitest **119/119**, typecheck, lint, strict page budget, i18n duplicate-key check, design-token check, production build, and `npm audit --omit=dev` (**0 vulnerabilities**) pass. Generated API/OpenAPI artifacts are unchanged. Build emits existing non-blocking dependency annotation and large-chunk warnings. No accounting, security, layering, scope, or contract-drift findings remain.

## Merge record

- **2026-07-18 — LL Git:** Commit `9f4f702` was rebased onto `origin/main`, pushed with lease protection, and merged as **PR #37** into `main`. P4-WP02 is complete. Next: P4-WP03 account detail / drill-down report page planning and implementation.
