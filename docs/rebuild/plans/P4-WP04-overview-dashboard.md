# P4-WP04 — Overview dashboard (server-computed)
  - **P3-WP04** (done) — tokens + shared primitives (`FormSection`, `DataTable`); the KPI tiles render through token-driven primitives (a small tile/card component may be added under `shared/`).
  - **P3-WP03** (done) — i18n corpus + duplicate-key gate + render-edge `formatMoney(minorUnits, currency, locale)`.
  - **P3-WP01** (done) — the app-shell router + `HomeRoute` (the placeholder the dashboard replaces) + `AppLayout` nav.
  - **P4-WP01** (design-system foundation) — **soft dependency** (renders through the token-driven primitives; inherits tokens automatically; no hard block), identical to P4-WP02/WP03.
- **Blocks:** nothing hard. M2 advanced-reporting/dashboard-drill parity (charts, pivots, budget-vs-actual, period comparison, monthly series) is **P4-WP26** and builds on this landing page.
## Spec sources

- `docs/architecture/rebuild/03-target-architecture.md` §72 (*server-computed dashboards, no derived-state drift* — a **new** capability, spec-derived rewrite not a port), target table line 83 (*Budgets & dashboards … SQL views/materialized views — server-computed, checksummable*), §4.2 (schema: signed `base_amount_minor`, `accounts.kind`, posted status), §7 (frontend features → application → api; TanStack Query; app shell owns routing/landing), §8.3 (RLS second wall for reads).
- `docs/architecture/rebuild/02-weaknesses.md` §2 line 41 (the `monthlySummaries` derived-summary drift this WP eliminates — the **motivation**, not a port).
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 4 (*budgets + dashboards (SQL views; kills derived-summary drift)*), §5 (salvage/rewrite: all data access → TanStack Query + generated client; the OLD UI-computed dashboards are not ported).
- `docs/architecture/rebuild/06-feature-roadmap.md` M1 Reports & dashboard (line 44 — *overview dashboard (server-computed)* is M1; cash-flow / pivots / drill-everything are M2).
- `docs/architecture/rebuild/07-vibe-coding-playbook.md` §107 (reuse shared primitives — the tiles render through token-driven primitives).
- `.github/instructions/backend.instructions.md` — module boundaries (EF/Npgsql stays in Ledger Infrastructure); RLS second wall; authorized read endpoints; money is integer minor units.
- `.github/instructions/frontend.instructions.md` — layering **features → application → api**; page budget **450 lines / 30 imports / 20 state hooks**; **money is integer minor units, formatted only at the render edge (never float math on amounts)**; i18n every string in all locales; IDs are opaque strings; reuse shared primitives; **desktop-first**.
- `docs/rebuild/plans/P2-WP07-reporting-views-integrity.md` — the `security_invoker` view pattern, the txn-local RLS-bound read, `ledger.read`, and the **C1 debit-positive / presentation-flip convention** the dashboard KPIs must match.
- `docs/rebuild/plans/P4-WP03-account-detail-drill-down-report-page.md` — the new-read-endpoint + read-page **WP03a → WP03b split** precedent this WP mirrors (service shape, endpoint registration, contract-record style, application wrapper/hook, reserved key, invalidation).
- `docs/rebuild/plans/P4-WP02-balance-sheet-income-statement-pages.md` / `P3-WP07-trial-balance-report-page.md` — the report-page pattern and the "render verbatim, never re-derive" guard.

## Backend design (WP04a — new, this WP adds it)

New read, **new `security_invoker` SQL view via a raw-SQL migration, no EF entity** (single-row-per-space aggregate; D-P4-DASH-SHAPE).

- **Migration (raw SQL, `Infrastructure/Migrations/…_DashboardSummaryView.cs`):** create `dashboard_summary` as a `security_invoker` view aggregating the existing RLS-wrapped `trial_balance` view (which already filters to `app.current_space_id` and reads the materialized `trial_balance_mat`), producing one row of KPI totals in the **C1 presentation convention** (assets positive; liabilities/equity/income flipped to positive; expenses positive). Grant `SELECT` to `leafledger_app`. `Down` drops the view. No column/table/EF-entity change → `HasPendingModelChanges()` stays green. Sketch:
  ```sql
  CREATE VIEW dashboard_summary
  WITH (security_invoker = true)
  AS
  SELECT
      COALESCE(SUM(base_balance_minor) FILTER (WHERE account_kind = 'asset'), 0)::bigint       AS total_assets_minor,
      COALESCE(SUM(-base_balance_minor) FILTER (WHERE account_kind = 'liability'), 0)::bigint   AS total_liabilities_minor,
      COALESCE(SUM(-base_balance_minor) FILTER (WHERE account_kind = 'equity'), 0)::bigint       AS total_equity_minor,
      COALESCE(SUM(-base_balance_minor) FILTER (WHERE account_kind = 'income'), 0)::bigint        AS total_income_minor,
      COALESCE(SUM(base_balance_minor) FILTER (WHERE account_kind = 'expense'), 0)::bigint         AS total_expenses_minor,
      COUNT(*)::bigint                                                                             AS account_count
  FROM trial_balance;
  GRANT SELECT ON dashboard_summary TO leafledger_app;
  ```
  `net_result_minor` (= income − expenses) and `net_worth_minor` (= assets − liabilities) are computed in the read service from these totals (or added as expression columns in the view — implementer's choice; both are server-computed and integer-safe). *(The exact equity-vs-net-result identity is pinned by C-P4-DASH-SUMMARY; see Accounting decisions.)*
- **Read service (Ledger Infrastructure):** `IDashboardService` + `DashboardService` mirroring `LedgerReportService` — one method `Task<DashboardSummaryReport> GetDashboardSummaryAsync(Guid spaceId, CancellationToken)` using the **same `OpenBoundConnectionAsync` txn-local RLS binding** (rollback-on-dispose). A single `SELECT … FROM dashboard_summary;` under the binding, plus a `balanced` flag read from the always-live integrity path (reuse `LedgerReportService.GetIntegrityAsync` / `trial_balance_live` sum) so the dashboard's "balanced" indicator is **exact**, not eventually-consistent. *(Alternatively expose `balanced` off the summary; the exact-integrity source is preferred — matches D-WP12-INTEGRITY.)*
- **Contract (`Application/Reporting/DashboardContracts.cs`, `LedgerReportContracts` style):**
  - `DashboardSummaryReport(Guid SpaceId, long TotalAssetsMinor, long TotalLiabilitiesMinor, long TotalEquityMinor, long TotalIncomeMinor, long TotalExpensesMinor, long NetResultMinor, long NetWorthMinor, int AccountCount, bool Balanced)`.
  - All amounts are **integer minor units** in the C1 presentation convention; no float.
- **Route:** `GET /api/v1/spaces/{spaceId:guid}/reports/dashboard` → `DashboardSummaryReport`, registered on the existing `reportGroup` in `LedgerEndpoints`, `.WithName("GetDashboardSummary")`, `.Produces<DashboardSummaryReport>(200)` + 401/403 ProblemDetails, authorized with **`ledger.read`** via the existing `configureAuthorization` seam (D-P4-DASH-PERM). Read-only; no idempotency header (GET). A space with no posted lines returns `200` with all-zero KPIs and `balanced = true` (never 404/leak).
- **OpenAPI/TS regeneration (REQUIRED):** rebuild `backend/openapi/leafledger-v1.json` and regenerate `app/src/api/schema.d.ts` so the frontend gets typed `DashboardSummaryReport` and the `GetDashboardSummary` operation. The CI `contract` diff gate must be green (committed artifacts match generated). A regen diff here is **expected and required**.
- **Register `IDashboardService` in `LedgerModule.cs`.**

## Frontend design (WP04b — the landing dashboard page)

- **Application layer — `app/src/application/reports.ts`** (extend): `getDashboardSummary(spaceId, client = apiClient)` wrapping `client.GET('/api/v1/spaces/{spaceId}/reports/dashboard', { params: { path: { spaceId } } })`, throwing on `data === undefined`, mapping to a frontend `DashboardSummary` view model (opaque string ids, integer minor units preserved — no float).
- **Query hook / key — `app/src/application/query/useDashboardSummary.ts` + `queryKeys.ts`:** add `qk.reports.dashboard: (spaceId) => ['reports','dashboard',spaceId]` (no such key reserved today), and `useDashboardSummary(spaceId)` = `useQuery` on it. Update `queryKeys.test.ts` + `application/query/README.md`.
- **Feature page — `app/src/features/reports/DashboardPage.tsx`** (or `app/src/features/dashboard/DashboardPage.tsx`): a read-only landing page rendering **KPI tiles** (total assets, total liabilities, total equity, income, expenses, **net result**, **net worth**, account count) through token-driven primitives (a small `shared/` tile/`StatCard` component may be added), plus a **balanced** indicator; loading (`role="status"`) / empty (all-zero) / error(boundary) states; all money via render-edge `formatMoney` (integer minor units in, localized string out — **no float, no re-derivation** in the component). ≤ 450 lines (page budget). **No charts, pivots, budget-vs-actual, period comparison, or drill** (M2 / P4-WP26).
- **Routing / nav — `app/src/app/router.tsx` + `AppLayout.tsx`:** make the dashboard the **landing page** — the existing placeholder `HomeRoute` (meta/version card) is replaced by the dashboard at `/` (D-P4-DASH-NAV), with a nav affordance in `AppLayout`. Update `router.test.tsx` / `shell.test.tsx`. *(The `getMeta()` anchor stays exercised elsewhere — the shell/version can move to a footer/about; do not delete the meta wiring.)*
- **Invalidation:** extend the P4-WP02/WP03 N6 wiring so a post refreshes an open dashboard — add `qk.reports.dashboard(spaceId)` to `usePostJournalEntry` success and to the `reports.trialBalance` topic in `invalidationMap.ts` (no backend/SignalR change; same rationale as the other report keys).
- **i18n:** all dashboard copy (title/eyebrow/description, each KPI tile label, balanced/unbalanced, loading/empty, nav) as keys in **both** `en.json` and `de.json`; duplicate-key gate green.

## Goal

A signed-in user lands on an **overview dashboard** — a one-glance set of server-computed KPI tiles (total assets, liabilities, equity, income, expenses, net result, net worth, account count) plus a balanced indicator for their space — every figure computed **on the server** from the same GL data as the trial balance / BS / P&L (no client-side derived-summary drift; checksummable). Concretely: WP04a exposes `GET …/reports/dashboard` → `DashboardSummaryReport` (new `security_invoker` `dashboard_summary` view over the materialized `trial_balance`, RLS-bound, `ledger.read`, OpenAPI/TS regenerated); WP04b renders it as the landing `DashboardPage` with KPI tiles, loading/empty/error states, nav, and post→see-it/live invalidation.

This WP adds **no** charts, pivots, budget-vs-actual, period comparison, monthly series, cash-flow statement, or drill (all M2 / P4-WP26); **no** date/period filtering; **no** account-level breakdown (that is the trial balance / BS / P&L / account-drill, already shipped).

## Scope

### WP04a — backend dashboard-summary read (LL Backend Dev)
1. `Infrastructure/Migrations/…_DashboardSummaryView.cs` — raw-SQL `security_invoker` `dashboard_summary` view over `trial_balance` + `GRANT SELECT`; `Down` drops it. No EF entity/column change.
2. `Application/Reporting/DashboardContracts.cs` — `IDashboardService`, `DashboardSummaryReport`.
3. `Infrastructure/DashboardService.cs` — RLS-bound single-row read of `dashboard_summary` + exact `balanced` flag, mirroring `LedgerReportService`.
4. `Infrastructure/LedgerEndpoints.cs` — register `GET /reports/dashboard` (`ledger.read`, `Produces<DashboardSummaryReport>(200)` + 401/403).
5. `Infrastructure/LedgerModule.cs` — register `IDashboardService`.
6. `backend/openapi/leafledger-v1.json` + `app/src/api/schema.d.ts` — regenerated (contract gate green).
7. Backend integration tests (`postgres:17`) — see acceptance criteria.

### WP04b — landing dashboard page (LL Frontend Dev)
8. `app/src/application/reports.ts` — `getDashboardSummary` + `DashboardSummary` view type.
9. `app/src/application/query/useDashboardSummary.ts` + `queryKeys.ts` (+ tests, README) — hook + reserved `dashboard` key.
10. `app/src/features/.../DashboardPage.tsx` (+ optional `shared/StatCard`) — landing page with KPI tiles + balanced indicator + states.
11. `app/src/app/router.tsx` (+ `router.test.tsx`) — dashboard as the `/` landing route (replacing the placeholder `HomeRoute`); nav in `AppLayout.tsx` (+ `shell.test.tsx`).
12. `app/src/application/query/usePostJournalEntry.ts` + `invalidationMap.ts` (+ tests) — add the dashboard key to post-success + `reports.trialBalance` topic.
13. `app/src/i18n/locales/en.json`, `de.json` — all dashboard copy in both locales.
14. Frontend tests — see acceptance criteria.

## Non-goals (explicitly deferred)
- **No charts / graphs / recharts** — M2 (P4-WP26 advanced reporting + dashboard drill parity).
- **No pivots, budget-vs-actual, period comparison, monthly/time series, top-counterpart panels** — M2.
- **No cash-flow statement** — M2.
- **No drill from the dashboard** — clicking through to the account ledger is P4-WP03's surface; the dashboard is headline totals only. (A later polish WP may add tile→report links; not here.)
- **No date/period/fiscal-year filtering** — the dashboard is whole-space current totals (matches the P2-WP07 whole-space views); period filtering is a later WP once periods-management UI (P4-WP15) lands.
- **No new refresh machinery** — the `dashboard_summary` view rides the existing P2-WP12 `trial_balance_mat` materialization + coalesced post-commit refresh; the exact `balanced` flag reads the always-live integrity path.
- **No new SQL beyond the one summary view / no EF entity** — raw-SQL migration only (D-P4-DASH-SHAPE).
- **No real space picker / `GET /spaces`** — reuse `VITE_DEMO_SPACE_ID` (D-P4-DASH-SPACE).

## Decisions (front-loaded — ALL APPROVED 2026-07-20)

Eight decisions. Seven are **non-accounting**; **C-P4-DASH-SUMMARY is the accounting confirmation** (Accounting decisions, below). All recommendations are approved without overrides. Each approved route is the smallest-diff continuation of merged precedent (P4-WP03 for the new-read split; P2-WP07/P4-WP02 for the presentation convention).

- **D-P4-DASH-SPLIT — one WP or two.** **APPROVED: split into WP04a (backend) → WP04b (frontend)**, mirroring P4-WP03a/b — the frontend types come from the regenerated OpenAPI, and the combined effort (new view + read + integration tests + regen, then a new landing page + tiles + nav/routing change + i18n + tests) exceeds ≤ 2 days. Each half is independently verifiable. *Alternative:* one combined WP — rejected (over the ceiling; the frontend hard-depends on the backend regen).
- **D-P4-DASH-SHAPE — new SQL view vs read-service C# composition.** **APPROVED: a new `dashboard_summary` `security_invoker` SQL view (raw-SQL migration, no EF entity)** over the already-RLS-wrapped, materialized-backed `trial_balance`. The spec is explicit (*"Dashboard summary SQL view(s)"*, *"SQL views/materialized views — server-computed, checksummable"*); a single-row aggregate needs no parameters (unlike P4-WP03), so a view expresses it cleanly and is DB-checksummable; it inherits the P2-WP12 materialization + refresh for free; `HasPendingModelChanges()` stays green (no entity). *Alternative:* aggregate in the C# read service over `trial_balance` rows with no migration — rejected (still server-computed, but less spec-faithful, not DB-checksummable, and duplicates the C1 sign logic in C# rather than reusing the DB presentation).
- **D-P4-DASH-ENDPOINT — route/shape.** **APPROVED: `GET /api/v1/spaces/{spaceId}/reports/dashboard` → `DashboardSummaryReport`** under the existing report group, tag `Ledger`. *Alternatives:* `…/dashboard/summary` (rejected — no second dashboard read at M1) or a top-level `…/dashboard` outside `/reports` (rejected — it is a report, and grouping under `/reports/*` matches every other read).
- **D-P4-DASH-PERM — authorization.** **APPROVED: reuse `ledger.read`** (all roles incl. Viewer), consistent with every other read. *Alternative:* a new permission — rejected (no access-control benefit at M1).
- **D-P4-DASH-CONTENT — which KPIs.** **APPROVED: the lean M1 set:** total assets, total liabilities, total equity, total income, total expenses, **net result** (income − expenses), **net worth** (assets − liabilities), account count, and a **balanced** indicator — all strict aggregations of the C1-approved BS/P&L figures (plus the two derived headlines governed by C-P4-DASH-SUMMARY). *Alternative:* add charts/trends/budget/top-partners now — rejected (M2 breadth; blows the ≤2-day ceiling and the page budget; the OLD chart monolith is exactly the not-to-port surface).
- **D-P4-DASH-NAV — where the dashboard lives.** **APPROVED: make the dashboard the landing page at `/`**, replacing the placeholder `HomeRoute` (meta/version card), with a nav affordance in `AppLayout`. It is literally the *"overview dashboard"* / home, and `HomeRoute` is a placeholder. Preserve the `getMeta()` wiring (move the version to a footer/about; do not delete it). *Alternative A:* a separate `/dashboard` route leaving `HomeRoute` at `/` — rejected (two landing surfaces; the overview *is* the home). *Alternative B:* modal/tab — rejected (a full landing page is the point).
- **D-P4-DASH-CURRENCY — base currency for render-edge formatting.** **APPROVED: reuse `VITE_DEMO_BASE_CURRENCY` (default `CHF`)** for all KPI money — identical seam to P4-WP02/WP03/P3-WP07. *Alternative:* infer a currency — rejected (never infer; amounts are integer minor units).
- **D-P4-DASH-SPACE — space selection.** **APPROVED: reuse `VITE_DEMO_SPACE_ID`** (identical to P3-WP05/06/07, P4-WP02/WP03). A real `GET /spaces` + picker is a later WP.

## Accounting decisions

**C-P4-DASH-SUMMARY — APPROVED 2026-07-20.** The per-kind KPIs (assets / liabilities / equity / income / expenses) are strict aggregations of the **already-approved P2-WP07 C1 presentation convention** (assets positive; liabilities/equity/income flipped to positive; expenses positive). The two derived headline identities are fixed as follows:
1. **Net result** = total income − total expenses, identical to the existing BS `Current result` / P&L `Net result` value; it is not a second accounting amount.
2. **Net worth** = total assets − total liabilities. The **equity KPI excludes the current-period result**, which is surfaced separately as `net_result_minor`; therefore the reconciliation is `Assets = Liabilities + Equity + Net Result`, equivalently `Net Worth = Equity + Net Result`.

The dashboard must receive these derived values from the server. The frontend formats integer minor-unit values only and performs no accounting arithmetic. Pin the decision with explicit-value integration tests reconciling the dashboard KPIs to the balance-sheet and income-statement endpoints; no golden fixtures are required.

`account_count` is defined as the count of accounts represented by the posted-ledger `trial_balance` relation. It therefore counts accounts with posted ledger rows, not every account in the chart of accounts; it must not be presented as a chart-account count.

**Standing guard (from P3-WP07 / P4-WP02 / P4-WP03, load-bearing here):** the client renders server-provided amounts **verbatim** and performs **no** sign flip, abs, aggregation, subtraction, or re-derivation. Net result and net worth are computed **server-side** (in the view/read service), never in the component. If any presentation rule cannot be traced to the server output or the C-P4-DASH-SUMMARY confirmation, **stop and route to LL Accounting Expert**. Two boundaries are respected, not decided: money stays integer minor units + ISO currency, formatted only at the render edge (no float math on amounts); the dashboard is read-only.

## Golden fixtures

**None required.** The dashboard ports no accounting engine and captures no OLD oracle: the OLD dashboard was **client-computed over the `monthlySummaries` Dexie cache** (a §72 new server capability, and the exact derived-summary drift the server-side view eliminates — no capturable pure function). Correctness is pinned at the correct tiers:
- **Backend:** explicit-value integration tests (`postgres:17`) — seed a space with posted lines across all five account kinds; assert every KPI equals the hand-computed C1 total; assert `netResultMinor` == the income-statement endpoint's `netResultMinor` and == the balance-sheet endpoint's `currentResultMinor`; assert `totalAssetsMinor` / `(totalLiabilitiesMinor + totalEquityMinor + netResultMinor)` reconcile per the confirmed C-P4-DASH-SUMMARY identity; a reversal nets correctly; RLS hides another space's totals; `ledger.read` enforced (401/403/Viewer-200); an empty space returns all-zero KPIs with `balanced = true`; `balanced` reflects the exact live integrity flag.
- **Frontend:** component/hook/route tests (loading/empty/error/tiles, verbatim money rendering, dashboard-as-landing route resolution + `RouteErrorBoundary`, invalidation wiring).
Recorded so QA does not expect a golden artifact.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e` (local checkout `C:\Programming\LeafLedger\Accounting`).

- **Reference only / non-port (§72 new capability):** `src/features/dashboard/DashboardPage.tsx` + `view-model/dashboardOverviewDataApi` + the chart/pivot/budget/drill/period-comparison machinery — client-computed over `monthlySummaries` (the drift being eliminated). This WP is a **spec-derived rewrite** — a thin landing page over a new server read, **not** a port. Only the abstract idea (a few headline financial figures on a landing page) informs the design; all of the OLD dashboard's charts/pivots/budget/drill are **M2 (P4-WP26)**.
- **New capability (no OLD oracle):** `GET …/reports/dashboard` + the `dashboard_summary` `security_invoker` view — pinned by integration tests reconciling to the merged BS/P&L/trial-balance endpoints, not a captured oracle.
- **Salvage (pattern, not code):** the `LedgerReportService` RLS-bound read shape + the `security_invoker` view pattern (WP04a); the `getBalanceSheet`/`useBalanceSheet` + `getTrialBalance`/`useTrialBalance` read-page shape, `DataTable`/`FormSection`/tokens, and the P3-WP03 i18n corpus (WP04b).

## Dependencies
- **No new production dependency** (backend uses existing Npgsql/EF; frontend uses existing React/TanStack Query/generated client). Any addition is a plan-file amendment before use.
- **New migration** (the one `dashboard_summary` view) — raw SQL, no EF entity, no new grant beyond the view's own `GRANT SELECT ON dashboard_summary`. The underlying `journal_lines`/`journal_entries`/`accounts` grants + RLS + the `trial_balance` materialization already exist (P2-WP02/WP07/WP12).
- OpenAPI/TS regeneration uses the existing P1-WP04 pipeline (**expected** to produce a diff here — the new `GetDashboardSummary` op — and must stay contract-gate green).

## File list (implementation target)

**WP04a — backend (new/modified)**
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/Migrations/…_DashboardSummaryView.cs` — raw-SQL `dashboard_summary` `security_invoker` view + grant.
- `backend/src/LeafLedger.Modules.Ledger/Application/Reporting/DashboardContracts.cs` — `IDashboardService`, `DashboardSummaryReport`.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/DashboardService.cs` — RLS-bound summary read + exact `balanced`.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerEndpoints.cs` — add `GET …/reports/dashboard` (`ledger.read`, `Produces<DashboardSummaryReport>(200)` + 401/403).
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerModule.cs` — register `IDashboardService`.
- `backend/openapi/leafledger-v1.json` — regenerated.
- `app/src/api/schema.d.ts` — regenerated (generated file; regenerate, do not hand-edit).

**WP04b — frontend (new/modified)**
- `app/src/application/reports.ts` — `getDashboardSummary` + `DashboardSummary` view type.
- `app/src/application/query/useDashboardSummary.ts` — hook.
- `app/src/application/query/queryKeys.ts` (+ `queryKeys.test.ts`, `application/query/README.md`) — reserve `dashboard` key.
- `app/src/features/.../DashboardPage.tsx` (+ optional `app/src/shared/StatCard.tsx`) — landing page with KPI tiles.
- `app/src/app/router.tsx` (+ `router.test.tsx`) — dashboard as the `/` landing route (replace placeholder `HomeRoute`).
- `app/src/app/AppLayout.tsx` (+ `shell.test.tsx` if asserted) — nav affordance; preserve `getMeta()` wiring.
- `app/src/application/query/usePostJournalEntry.ts` + `invalidationMap.ts` (+ tests) — dashboard invalidation.
- `app/src/i18n/locales/en.json`, `de.json` — dashboard copy (both locales).

## Acceptance criteria (concrete tests — all must pass)

**WP04a — backend**
1. **Endpoint returns the space's server-computed KPIs.** An integration test seeds a space with posted lines across all five account kinds; `GET …/reports/dashboard` returns `DashboardSummaryReport` with each KPI equal to the hand-computed C1 total (assets positive; liabilities/equity/income/expenses per the C1 convention) and the correct `accountCount`.
2. **Derived headlines reconcile (per confirmed C-P4-DASH-SUMMARY).** `netResultMinor` == the income-statement endpoint's `netResultMinor` == the balance-sheet endpoint's `currentResultMinor`; `netWorthMinor` == `totalAssetsMinor − totalLiabilitiesMinor`; the confirmed equity/net-result identity holds (`totalAssetsMinor` reconciles to `totalLiabilitiesMinor + totalEquityMinor + netResultMinor`).
3. **Reversal nets naturally.** After a reversal entry, the KPIs reflect the net posted state (no special-casing).
4. **Tenancy (RLS second wall).** A second space's totals never appear in space A's request (two seeded spaces); the summary reflects only the bound space.
5. **Balanced flag is exact.** `balanced` == the live-integrity `balanced` (reads the always-live path, not the eventually-consistent materialized path); a balanced ledger ⇒ `true`.
6. **Authorization.** Anonymous → 401; a principal without `ledger.read` → 403; a **Viewer** member → 200 (reusing the WP06 auth harness).
7. **Empty space.** A space with no posted lines returns `200` with all-zero KPIs and `balanced = true` (never 404/500/leak).
8. **No schema/model drift + contract regenerated.** The new view adds no EF entity; `HasPendingModelChanges()` no-drift stays green; the migration `Up`/`Down` round-trips; `backend/openapi/leafledger-v1.json` + `app/src/api/schema.d.ts` regenerate and are committed; the CI `contract` gate is green; the new `GetDashboardSummary` path + `DashboardSummaryReport` schema are present.

**WP04b — frontend**
9. **`getDashboardSummary` mapping** — `reports.test.ts`: given a mocked client response, returns the `DashboardSummary` view model with integer minor units (no float); throws when `data === undefined`.
10. **Hook + reserved key** — `useDashboardSummary.test.tsx` / `queryKeys.test.ts`: the hook uses `qk.reports.dashboard(spaceId)` and calls the wrapper; `qk.reports.dashboard('sp')` ⇒ `['reports','dashboard','sp']`.
11. **`DashboardPage` states** — `DashboardPage.test.tsx`: loading (`role="status"`), empty (all-zero KPIs), and populated states render; every KPI + the balanced indicator render **verbatim** via `formatMoney` (integer minor units in, localized string out, no float / no re-derivation); a query error throws to the route error boundary.
12. **Dashboard is the landing page** — `router.test.tsx` / `shell.test.tsx`: `/` resolves to the dashboard under `AppLayout` (the placeholder `HomeRoute` is replaced); nav affordance present; a query failure renders the real `RouteErrorBoundary`; the `getMeta()` wiring remains exercised (footer/about), not deleted.
13. **Invalidation** — `usePostJournalEntry.test.tsx` / `invalidationMap.test.ts`: a successful post and a `reports.trialBalance` SignalR ping both invalidate `qk.reports.dashboard(spaceId)`, refreshing an open dashboard without a backend change.
14. **i18n presence** — every new dashboard/nav key exists in **both** `en.json` and `de.json`; the duplicate-key build gate is green.

**Both halves**
15. **Gates green.** Backend: Release build `0/0`, architecture `3/3`, new + existing integration tests pass (Docker), migration `Up`/`Down` round-trips. Frontend: lint, typecheck, full Vitest, strict page budget (`DashboardPage` ≤ 450/30/20), production build, duplicate-key check, design-token check, and `npm audit --omit=dev` (0 vulnerabilities) pass. Layering (features → application → api) and Ledger-Infrastructure boundary intact.

## Split seam (recommended execution)

- **WP04a (LL Backend Dev):** migration (`dashboard_summary` view) + contract + `DashboardService` + endpoint + module registration + integration tests + OpenAPI/TS regen. Independently verifiable (view/RLS/authz/reconciliation tests + contract gate + `Up`/`Down` round-trip). **Do this first** (the frontend types come from the regen); the confirmed C-P4-DASH-SUMMARY identity is an implementation constraint.
- **WP04b (LL Frontend Dev):** `getDashboardSummary`/`useDashboardSummary` + reserved key + `DashboardPage` (KPI tiles) + landing-route swap + nav + invalidation + i18n + tests, against the regenerated types. Independently verifiable (component/route/hook tests, page budget).

## Open questions / carry-forwards
- **KPI derived-identity semantics** — resolved by the approved C-P4-DASH-SUMMARY decision above; WP04a is unblocked.
- **Charts / pivots / budget-vs-actual / period comparison / dashboard drill** — M2 (P4-WP26 advanced reporting + dashboard drill parity); this WP is the lean server-computed landing page they will later enrich.
- **Real space selection** — `VITE_DEMO_SPACE_ID` is a slice-only stand-in; a real `GET /spaces` + picker is a later WP (shared with P3-WP05/06/07, P4-WP02/WP03).
- **Date/period filtering on the dashboard** — deferred until periods-management UI (P4-WP15) lands.
- **Tile → report drill links** (click a KPI to open the matching report) — a later polish WP; not in M1 scope here.

## Definition of done
All 15 acceptance criteria pass; state → `verify`; LL QA Reviewer acceptance review (each half); then LL Git. Backend: new `dashboard_summary` view + RLS + authz + regenerated contract, no EF drift, `Up`/`Down` round-trips. Frontend: landing dashboard page + KPI tiles + nav + invalidation + i18n, layering and page budgets intact. Golden fixtures: none. Accounting confirmation C-P4-DASH-SUMMARY: recorded and applied.

## QA verdict
**PASS — WP04a remediation re-reviewed 2026-07-20 by LL QA Reviewer.** All five endpoint-specific evidence findings are closed: `LedgerReportTests.cs` now proves dashboard reversal netting, two-space tenancy isolation, direct anonymous/missing-`ledger.read`/Viewer authorization behavior, and the exact live `balanced` path; `MaterializedReportMigrationDownTests.cs` explicitly proves `dashboard_summary` exists after `Up` and is absent after `Down`. The fixture SQL uses independent scalar subqueries so both journal statements resolve their account IDs correctly.

Validation: focused dashboard and migration tests `9/9`; full backend Release suite `357/357`; OpenAPI regeneration and frontend typecheck pass; `git diff --check` is clean. No production-code defect was identified. WP04b is now independently PASS and `verify`; the unrelated modification to `P4-WP03-account-detail-drill-down-report-page.md` remains outside this WP04a review.

**PASS — WP04b remediation re-reviewed 2026-07-20 by LL QA Reviewer.** Both findings are closed: the empty dashboard state now renders the server-provided `balanced` flag, with an all-zero/unbalanced regression assertion, and the post-success mutation test directly asserts dashboard query invalidation. Focused remediation tests pass `5/5`; full frontend Vitest passes `139/139`; lint, typecheck, strict page budget, i18n duplicate-key, design-token, production build, `npm audit --omit=dev` (0 vulnerabilities), and `git diff --check` pass. WP04b state → `verify`; the unrelated modification to `P4-WP03-account-detail-drill-down-report-page.md` remains outside this review.

**PASS — WP04b current-state re-review 2026-07-20 by LL QA Reviewer.** Reproduced the complete focused evidence set at `36/36` and the full frontend suite at `139/139`. Lint, typecheck, strict page budget, i18n duplicate-key, design-token, production build, `npm audit --omit=dev` (0 vulnerabilities), and `git diff --check` all pass. No new findings; WP04b remains `verify`.

## Implementation log
- **2026-07-20 — LL Frontend Dev:** WP04b implemented → `verify`. Added the generated-client dashboard wrapper, reserved query key/hook, post and SignalR invalidation, lazy dashboard landing route, KPI page with server-value-preserving render-edge money formatting, loading/empty/error/integrity states, and EN/DE copy. Focused dashboard/router tests pass `19/19`; full frontend Vitest passes `139/139`; lint, typecheck, strict page budget, i18n duplicate-key, design-token, production build, and `git diff --check` pass. Next: LL QA Reviewer verifies WP04b.
- **2026-07-20 — LL Backend Dev:** WP04a implementation in progress. Added the RLS-bound `DashboardService`, raw-SQL `security_invoker` `dashboard_summary` migration, `ledger.read` GET endpoint, DI registration, explicit C1/reconciliation/empty-space integration assertions, and regenerated OpenAPI/TypeScript artifacts. Release host and integration-test builds pass; architecture tests pass `3/3`, Ledger unit tests pass `88/88`, frontend typecheck passes, and `git diff --check` is clean. PostgreSQL acceptance tests were attempted but are blocked at Testcontainers fixture construction because Docker is unavailable in the environment; state remains `in-progress`. Next: rerun WP04a PostgreSQL integration and migration Up/Down gates with Docker available.
- **2026-07-20 — LL Backend Dev:** Docker became available and WP04a acceptance completed. Focused report integration tests pass `5/5`; migration/no-drift tests pass `21/21`; full Release backend suite passes `357/357`; architecture tests pass `3/3`; Ledger unit tests pass `88/88`; frontend typecheck and OpenAPI/TS regeneration pass; `git diff --check` is clean. WP04a state → `verify`; next: LL QA Reviewer verifies WP04a, then LL Frontend Dev implements WP04b.
- **2026-07-20 — LL Backend Dev:** Closed all five QA evidence findings with endpoint-specific integration coverage: dashboard reversal netting, two-space tenancy isolation, direct dashboard authorization (anonymous, missing scope, Viewer), live `balanced` behavior, and explicit migration `Up`/`Down` catalog assertions. Fixed the tenant fixture's cross-statement CTE scope bug with scalar account subqueries. Focused remediation tests pass `9/9`; full backend Release suite passes `357/357`; OpenAPI regeneration, frontend typecheck, and `git diff --check` pass. QA verdict → PASS; WP04a state → `verify`. Next: LL QA Reviewer accepts WP04a, then LL Frontend Dev implements WP04b.
- **2026-07-20 — User / LL Architect:** All eight P4-WP04 recommendations approved without overrides, including the split WP04a→WP04b, `dashboard_summary` SQL view shape, `/reports/dashboard` route, `ledger.read`, lean M1 KPI set, dashboard landing route, `VITE_DEMO_BASE_CURRENCY`, and `VITE_DEMO_SPACE_ID`. **C-P4-DASH-SUMMARY approved:** `net_result = income − expenses`; `equity` excludes current result; `net_worth = assets − liabilities`; reconciliation `Assets = Liabilities + Equity + Net Result`; all derived values are server-computed and the client only formats integer minor units. `account_count` means accounts represented in posted-ledger `trial_balance`, not total chart accounts. Golden fixtures remain none; explicit-value integration tests must reconcile the dashboard to BS/P&L/trial-balance. State remains `planned`, unblocked. Next: LL Backend Dev implements WP04a.
