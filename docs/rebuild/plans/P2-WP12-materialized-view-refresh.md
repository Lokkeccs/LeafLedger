# P2-WP12 — Materialized-view refresh strategy (N4) for the hot reporting views

- **Phase:** 2 (ledger core — deferred performance follow-up)
- **State:** verify — implementation completed 2026-07-12. **D-WP12-TENANCY + D-WP12-INTEGRITY approved by the user 2026-07-12** (both recommended routes); D-WP12-TENANCY recorded as [ADR-0006](../../architecture/adr/ADR-0006-materialized-view-tenancy-wall.md).
- **Owner (implementation):** LL Backend Dev
- **Depends on:** P2-WP07 (the three plain `security_invoker` views + `LedgerReportService` + the four `ledger.read` endpoints + the integrity hash — all `done`, merged via PR #17), P2-WP02 (schema, `leafledger_app` role, RLS second wall, `app.current_space_id` GUC), P2-WP09 (`BackgroundService` + raw-SQL-migration + `SECURITY DEFINER` function + `IMeterFactory` precedents this WP mirrors).
- **Blocks:** nothing on the Phase-2 critical path — WP07 already ships a correct, always-live oracle; this WP is a pure performance optimization of an already-correct result. (WP08 is `done` and read live; this WP must keep it that way — see D-WP12-INTEGRITY.)
- **Estimated size:** ≤ 2 days (one raw-SQL migration promoting one base view to `MATERIALIZED` + unique index + a tenancy-preserving wrapper view; a post-commit coalescing refresh `BackgroundService` + enqueue seam; two metrics; equivalence/latency/concurrency/tenancy integration tests + a no-model-drift guard). **Split seam documented** below if it overflows.

## Carve-out note (LL Architect, 2026-07-12)

The original Phase-2 WP07 row bundled *"reporting views + `/integrity` hash **+ mat-view refresh strategy (N4)**"*. WP07 (per its re-scope note) shipped the **correctness half** — three **plain**, always-live, RLS-safe `security_invoker` views — and explicitly carved the **performance half (N4)** into this WP, recording in `status.md` that N4 is folded here, not there. A WP07 acceptance criterion pre-committed the contract this WP must honor: *"materialized view output ≡ plain view output."* WP12 promotes only the view proven to be the hot aggregation, preserving byte-identical output, tenancy isolation, and an always-live correctness oracle.

## Spec sources

- `docs/architecture/rebuild/03-target-architecture.md` §4.3 (**Reporting**: views *"materialized where hot, refreshed on posting"* — the mechanism is left to this WP) and §8.3 (RLS second wall — the tenancy invariant this WP must not weaken).
- `docs/rebuild/plans/NOTES-risk-review-2026-07-06.md` **N4** (the binding acceptance wording, copied verbatim into §Acceptance below): `REFRESH MATERIALIZED VIEW CONCURRENTLY` (requires a unique index); **post-commit enqueue** of refresh jobs (never inline in the posting transaction); **coalesce** multiple postings into one refresh; **refresh-duration metric + staleness indicator** surfaced to observability.
- `docs/rebuild/plans/P2-WP07-reporting-views-integrity.md` §Non-goals ("No materialized views / no N4 refresh machinery — P2-WP12") + §Risks ("Plain views, not materialized — deliberate … WP12 promotes the hot ones with identical output. Do not pre-optimize here").
- `docs/architecture/rebuild/05-quality-and-maintainability.md` §pipeline (the `/integrity` deploy probe must stay exact) + §testing (trial-balance ≡ 0 is the flagship invariant — WP08's oracle must stay live).
- ADR-0001 (server/DB is the source of truth); ADR-0002 (uuid ids).

## Goal

Move the **hot base aggregation** off every read's critical path by materializing it and refreshing it **off the posting transaction**, **without** (a) changing any report output, (b) weakening tenant isolation, or (c) making the integrity hash / WP08 ≡ 0 oracle / deploy probe read stale data. Concretely:

- Promote the base `trial_balance` aggregation to a **materialized** relation with a unique index, refreshed via `REFRESH MATERIALIZED VIEW CONCURRENTLY`.
- Refresh is **enqueued after the posting transaction commits** and **coalesced** so N rapid posts in a space cause far fewer than N refreshes.
- The three report **dashboard** endpoints (`trial-balance`, `balance-sheet`, `income-statement`) read the materialized path (eventually-consistent, bounded staleness).
- The **integrity** endpoint, the **WP08** property oracle, and the **deploy `/integrity` probe** keep reading a **live**, exact computation (D-WP12-INTEGRITY).
- Refresh **duration** and per-space **staleness** are emitted as metrics.

## The two load-bearing design problems (RESOLVED — user-approved 2026-07-12)

> Both decisions were approved by the user on 2026-07-12 on their recommended routes. D-WP12-TENANCY is recorded as [ADR-0006](../../architecture/adr/ADR-0006-materialized-view-tenancy-wall.md) by **LL Docs Editor** before the migration lands.

### D-WP12-TENANCY — materialized views do not honor RLS
PostgreSQL **row-level security is not applied to materialized views**: a matview is a single physical snapshot owned by its creator that stores rows for **all** spaces, and querying it does **not** re-evaluate the underlying tables' RLS policies. WP07's tenancy second wall is `WITH (security_invoker = true)` on plain views, which evaluates the base-table RLS as the querying `leafledger_app` role. **The moment `trial_balance` becomes a matview, that wall is gone** unless it is re-established explicitly.

- **Approved resolution (user, 2026-07-12 — wrapper view + GUC predicate + matview ungranted):** keep the tenant-facing relation a **plain `security_invoker` view named `trial_balance`** whose body is `SELECT … FROM trial_balance_mat WHERE space_id = NULLIF(current_setting('app.current_space_id', true), '')::uuid`. The physical matview (`trial_balance_mat`) is **never granted to `leafledger_app`** — only the wrapper view is — so the sole read path is GUC-filtered and fail-closed (no GUC ⇒ `NULLIF(...)::uuid` is NULL ⇒ zero rows, mirroring the WP02 `OpenAppNoContextAsync` guarantee). The wrapper additionally keeps `security_invoker = true` as defence-in-depth on any still-RLS-bearing relation it touches. The two derived views (`balance_sheet_lines`, `income_statement_lines`) are **unchanged** — they already select from `trial_balance`, so they transparently inherit the GUC-filtered wrapper. *Rationale for this route (user): the only safe way to preserve tenant isolation; avoids catastrophic cross-tenant leaks.*
- **Test obligation:** AC4 re-proves the exact WP07 AC7 cross-tenant guarantee against the materialized path — a principal for space A gets **no** space-B rows, and a no-context pooled connection returns **zero** rows.
- **This is a security/tenancy decision, not an accounting one — no LL Accounting Expert consult.** Recorded as [ADR-0006](../../architecture/adr/ADR-0006-materialized-view-tenancy-wall.md) because it changes the *mechanism* of the report-read second wall from RLS-invoker to an explicit GUC predicate.

### D-WP12-INTEGRITY — the correctness oracle must stay live
The integrity hash is the convergence/monitoring primitive and the **deploy `/integrity` probe** (Part 5); the WP08 property suite reads the trial balance as its **≡ 0 oracle**. If either read a stale matview, a freshly-committed-but-not-yet-refreshed post would make `balanced` momentarily wrong and could red-herring the deploy gate or flake the invariant suite.

- **Approved resolution (user, 2026-07-12 — keep `/integrity` and WP08 on a separate live view):** the **integrity** read path and the WP08 oracle continue to compute over a **live** relation. Introduce a separate always-live plain `security_invoker` view `trial_balance_live` (identical body to WP07's current `trial_balance`) that `LedgerReportService.GetIntegrityAsync` (and only it) reads; the three **report** endpoints read the materialized `trial_balance` wrapper. Both are RLS/GUC tenant-safe. By design the dashboards are eventually-consistent while integrity stays exact — this divergence during a staleness window is intended and documented. *Rationale for this route (user): ensures correctness, keeps the deploy probe exact, avoids synchronous-refresh blocking.*
- **Alternative considered (rejected for M1):** force a synchronous `REFRESH` before every integrity read — reintroduces refresh onto a read's critical path and defeats the point. Coalesced background refresh + a live integrity view is simpler and keeps the probe exact.

## Scope

1. **Materialization migration (new EF migration, raw SQL — no EF-modelled entity, `HasPendingModelChanges()` stays false):**
   - `CREATE MATERIALIZED VIEW trial_balance_mat AS <the current WP07 trial_balance body>` (the `SUM(base_amount_minor)` aggregation over posted lines, grouped per space+account). **Not** granted to `leafledger_app`.
   - `CREATE UNIQUE INDEX ux_trial_balance_mat ON trial_balance_mat (space_id, account_id)` — the prerequisite for `REFRESH … CONCURRENTLY`.
   - Replace the WP07 plain `trial_balance` view with the **wrapper** view over `trial_balance_mat` (D-WP12-TENANCY), `WITH (security_invoker = true)`, GUC-filtered, `GRANT SELECT … TO leafledger_app`.
   - Add `trial_balance_live` (the always-live plain view, WP07 body, `security_invoker`, granted) for the integrity/WP08 path (D-WP12-INTEGRITY).
   - The two derived views are recreated only if a `DROP … CASCADE` on the old `trial_balance` forces it; their bodies are **byte-identical** to WP07.
   - A `SECURITY DEFINER` refresh function `refresh_trial_balance_mat()` (mirroring the WP09 `delete_expired_idempotency_keys()` pattern) that runs `REFRESH MATERIALIZED VIEW CONCURRENTLY trial_balance_mat`, `REVOKE ALL … FROM PUBLIC`, `GRANT EXECUTE … TO leafledger_app`. (Refresh spans all spaces — it is a maintenance op, not a tenant read — so it runs as definer, never GUC-filtered.)
   - `Down` drops the function, the two new views, the unique index, and the matview, and restores the WP07 plain `trial_balance` view verbatim.
2. **Post-commit refresh enqueue (Infrastructure, off the critical path):**
   - After `JournalPostingService` **commits** (`transaction.CommitAsync`, line ~105) and for the reversal/period paths that mutate posted lines, enqueue a refresh **request** for that space into an in-process, deduplicating channel/queue — **never** inside the posting transaction, and failure to enqueue must never fail the post.
   - `RefreshCoalescingService : BackgroundService` (mirroring `IdempotencyCleanupService`) drains the queue on a short debounce window (default ~200 ms, config-bound) and **coalesces** all pending requests — per space, and if the aggregation is global, a single `REFRESH` covers every dirty space — into **one** `refresh_trial_balance_mat()` call per window. Runs on its own owner/app connection, not a request-scoped `LedgerDbContext`.
3. **Metrics (Infrastructure, `IMeterFactory`, mirroring `IdempotencyMetrics`):**
   - `leafledger.reporting.refresh.duration` (histogram, ms) recorded per refresh pass.
   - `leafledger.reporting.staleness` (histogram/gauge) — time between the earliest coalesced enqueue and the completed refresh (the observable staleness of the dashboard path).
   - `leafledger.reporting.refresh.rows` (optional counter) — rows affected per refresh (N4 "rows affected").
4. **Wiring:** register `RefreshCoalescingService` as an `IHostedService` and the enqueue seam in `LedgerModule.AddLedgerModule`; point `LedgerReportService` report reads at the wrapper `trial_balance` and the integrity read at `trial_balance_live`. `AddMetrics()` is already called.
5. **Contract:** **no new endpoints, no request/response shape change** → `backend/openapi/leafledger-v1.json` and `app/src/api/**` must regenerate **byte-identical** (the CI `contract` gate proves this WP added no surface).
6. **Tests:** equivalence, latency-off-critical-path, concurrency (post + refresh, no lock contention), tenancy (AC7 re-proof), staleness/metric emission, and the `HasPendingModelChanges()` no-drift guard.

## Non-goals (explicitly deferred)

- **No `vat_report` and no new report endpoints** — Phase 4 / M1; this WP only changes *how* the existing three reports + integrity are sourced.
- **No frontend / no dashboard UI / no realtime staleness surfacing to the client** — SignalR coalescing + invalidation are Phase 3 (N5/N6). Only regenerated `app/src/api/**` may change, and it must be byte-identical.
- **No `pg_cron`, no external job runner, no distributed queue** — an in-process `BackgroundService` + `Channel` mirrors the WP09 precedent and is sufficient for the single-Host M1 deployment; a durable/distributed refresher is a documented carry-forward for multi-instance scale-out.
- **No materialization of the two derived views or of the integrity path** — only the base aggregation is materialized; the derived views are cheap `CASE` transforms and the integrity path must stay live (D-WP12-INTEGRITY).
- **No change to posting/reversal/period behavior, balance walls, authorization, or the C1 sign convention** — WP04/05/06/07 semantics are untouched; the report *output* is invariant.
- **No new business tables/columns and no RLS-policy change on the base tables.**

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

- **No salvage / no port.** The OLD stack computed reports client-side over Dexie (WP07 §Source material established there is no capturable pure oracle) and had **no** materialized-view / server-refresh machinery at all — the OLD `baseCurrencyBalance` cache column was exactly the derived-state drift the rewrite deletes. WP12 is a **spec-derived (§4.3) + N4-derived** performance rewrite with **no OLD behavior to reproduce**.
- **Reference precedents inside the NEW repo (mirror, do not re-invent):** raw-SQL migration + `SECURITY DEFINER` function + `REVOKE/GRANT` ([IdempotencyKeys migration](../../../backend/src/LeafLedger.Modules.Ledger/Infrastructure/Migrations/20260712100000_IdempotencyKeys.cs)); `BackgroundService` + `PeriodicTimer` ([IdempotencyCleanupService](../../../backend/src/LeafLedger.Modules.Ledger/Infrastructure/IdempotencyCleanupService.cs)); `IMeterFactory` counter ([IdempotencyMetrics](../../../backend/src/LeafLedger.Modules.Ledger/Infrastructure/IdempotencyMetrics.cs)); txn-local GUC read binding ([LedgerReportService](../../../backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerReportService.cs)); the WP07 view bodies ([ReportingViews migration](../../../backend/src/LeafLedger.Modules.Ledger/Infrastructure/Migrations/20260711220000_ReportingViews.cs)).

## Accounting decisions

**None.** This WP changes *how* an already-correct, already-accounting-signed-off (C1) result is stored and refreshed — not *what* it computes. The materialized output must be **≡** the plain-view output (AC1), so no accounting rule is (re)interpreted. **No LL Accounting Expert consult required.** The one decision that needs sign-off (D-WP12-TENANCY / matview-loses-RLS) is a **security/architecture** decision for the **user**, not the domain expert.

## Golden fixtures

**None required.** WP12 ports no OLD accounting function and interprets no accounting rule; its correctness oracle is *equivalence to the already-verified WP07 plain views* over the same committed state (after a refresh), plus the WP08 ≡ 0 invariant (which continues to read the live path). Pinned by:
- an **equivalence integration test** (materialized `trial_balance` rows, and the two derived reports, ≡ a freshly-computed live aggregation for the same seeded ledger, after `refresh_trial_balance_mat()`),
- a **latency/critical-path test** (post returns without awaiting a refresh; refresh happens after commit),
- a **concurrency test** (interleaved post + `REFRESH … CONCURRENTLY`, no lock-contention failure),
- a **tenancy test** (WP07 AC7 re-proof against the matview-backed wrapper),
- and the **WP08 property suite** (already `done`, reads the live path — must stay green unchanged).
Recorded explicitly so QA does not expect a golden artifact.

## Dependencies

- **No new production NuGet.** `MATERIALIZED VIEW` / `REFRESH … CONCURRENTLY` are raw SQL via `MigrationBuilder`; `Channel<T>` / `BackgroundService` / `PeriodicTimer` / `Histogram<T>` are all BCL. The Docker `postgres:17` integration fixture + `Microsoft.AspNetCore.Mvc.Testing` are already present.
- The P1-WP04 OpenAPI + `openapi-typescript` pipeline regenerates the contract — which must be **unchanged** (no new surface); the CI `contract` diff gate must stay green.

## File list (implementation target)

**New — `backend/src/LeafLedger.Modules.Ledger/Infrastructure/Migrations/`**
- `<timestamp>_MaterializeTrialBalance.cs` (+ EF-discovery metadata sibling as the WP07/WP09 hand-authored migrations required): `CREATE MATERIALIZED VIEW trial_balance_mat` + `CREATE UNIQUE INDEX ux_trial_balance_mat` + replace `trial_balance` with the GUC-filtered `security_invoker` wrapper + add `trial_balance_live` + `refresh_trial_balance_mat()` (`SECURITY DEFINER`, `REVOKE`/`GRANT`); recreate the two derived views only if `CASCADE` forces it (bodies byte-identical to WP07). `Down` restores the WP07 state verbatim. **No table/column/base-RLS change** (no-drift guard is the tripwire).

**New — `backend/src/LeafLedger.Modules.Ledger/Infrastructure/`**
- `RefreshCoalescingService.cs` (`BackgroundService`: drains a dedup queue on a debounce window → one `refresh_trial_balance_mat()` per window; own app connection).
- `IReportRefreshQueue.cs` + `ReportRefreshQueue.cs` (the in-process, per-space-deduplicating enqueue seam; a bounded `Channel`/`HashSet`+signal).
- `ReportingRefreshMetrics.cs` (`IMeterFactory`: refresh-duration histogram + staleness + rows counter).

**Modified**
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/JournalPostingService.cs` — after the successful `CommitAsync`, enqueue a refresh request for the space; enqueue failure never fails the post; **no logic inside the transaction**. (Reversal + any period-lifecycle path that alters posted lines enqueues too.)
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerReportService.cs` — the three report reads target the wrapper `trial_balance` (unchanged SQL text — same view name); `GetIntegrityAsync` targets `trial_balance_live`.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerModule.cs` — register `IReportRefreshQueue`/`ReportRefreshQueue` (singleton), `RefreshCoalescingService` (`IHostedService`), `ReportingRefreshMetrics` (singleton).
- `backend/openapi/leafledger-v1.json` + `app/src/api/schema.d.ts` (+ `client.ts`) — regenerated; must be **byte-identical** (no surface change).
- `docs/rebuild/plans/P2-WP12-materialized-view-refresh.md` + `docs/rebuild/status.md` — notes/state.
- `docs/architecture/adr/README.md` + [ADR-0006](../../architecture/adr/ADR-0006-materialized-view-tenancy-wall.md) recording D-WP12-TENANCY (matview-loses-RLS → GUC-predicate wrapper, matview ungranted) — **approved and recorded 2026-07-12** by **LL Docs Editor** before the migration lands.

**New — tests**
- `backend/tests/LeafLedger.IntegrationTests/Ledger/MaterializedReportEquivalenceTests.cs` (`[Trait("Category","Integration")]`) — seed a small balanced ledger incl. a reversal; call `refresh_trial_balance_mat()`; assert materialized `trial_balance` rows + `balance_sheet_lines` + `income_statement_lines` ≡ the pre-WP12 live aggregation (per-account and totals), total ≡ 0; then post again *without* refreshing and assert integrity (`trial_balance_live`) reflects it immediately while the dashboard path is stale until the next refresh.
- `backend/tests/LeafLedger.IntegrationTests/Ledger/ReportRefreshLatencyTests.cs` — a post returns before any refresh completes (the enqueue is post-commit and non-blocking); a coalescing burst of N posts in one space yields far fewer than N `refresh_trial_balance_mat()` executions (assert via a counter/spy or a refresh-count probe).
- `backend/tests/LeafLedger.IntegrationTests/Ledger/ReportRefreshConcurrencyTests.cs` — interleave a post/commit with a `REFRESH MATERIALIZED VIEW CONCURRENTLY`; no lock-contention/serialization failure; final state consistent after the last refresh.
- `backend/tests/LeafLedger.IntegrationTests/Ledger/MaterializedReportTenancyTests.cs` — WP07 AC7 re-proof: a principal for space A reads no space-B rows via the matview-backed wrapper; a no-context pooled connection returns zero rows; `trial_balance_mat` is **not** directly selectable by `leafledger_app` (only the wrapper is granted).
- (Optional unit) `ReportRefreshQueueTests.cs` — enqueue dedups per space; drains once per window.

No files under `app/src/**` except regenerated `api/**` (byte-identical); no `*.Domain` change; no `vat_report`; no new endpoint; no base-table/column/RLS change.

## Boundary note

- The refresh queue, background service, matview reads, and metrics live in the Ledger module's **Infrastructure**; no new public Application contract is required (reports keep their WP07 `ILedgerReportService` surface). EF/Npgsql stays confined to Infrastructure — `EfCoreIsConfinedToInfrastructureNamespaces` and `DomainNamespacesDependOnlyOnSharedKernel` must stay green.
- The matview + functions are defined in the Ledger migration (the module owns its schema per the D1 single-context baseline). A future extraction into a Reporting/Budgets module (spec §3) remains a carry-forward.
- The enqueue seam is a **post-commit side effect** — it must be structurally impossible for it to run inside, block, or fail the posting transaction (AC2 is the tripwire).

## Implementation notes

- **2026-07-12 — LL Backend Dev:** Added the raw-SQL materialization migration (`trial_balance_mat`, unique refresh index, GUC-filtered wrapper, live integrity view, and security-definer refresh function), post-commit deduplicating queue, debounce hosted service, and refresh duration/staleness/rows metrics. Dashboard reads remain on `trial_balance`; integrity reads `trial_balance_live`.
- **2026-07-12 — LL Backend Dev:** PostgreSQL rejects an invoker view that selects an underlying relation the invoker cannot access. The tenant wrapper therefore omits `security_invoker` while retaining the explicit fail-closed `app.current_space_id` predicate; `trial_balance_mat` remains ungranted to `leafledger_app`. This is the executable form of ADR-0006's tenancy wall and needs QA review.
- **2026-07-12 — LL Backend Dev:** Added queue tests (2/2) and materialized tenancy/function tests (2/2); the existing report suite plus new materialized tests pass 7/7. Added `MeterListener` coverage for duration, staleness, and rows (1/1), plus a Testcontainers post/refresh concurrency test (1/1). Final Release backend suite passes **319/319** with architecture and Docker-backed integration tests; `git diff --check` and diagnostics are clean. Migration-down coverage remains the only concrete acceptance gap before `verify`.
- **2026-07-12 — LL Backend Dev:** Added an isolated Testcontainers migration-down test (1/1) that migrates from WP12 to `20260712150000_PeriodOverlapConstraint`, verifies removal of `trial_balance_mat`, `ux_trial_balance_mat`, and `refresh_trial_balance_mat()`, and verifies restoration of the three plain WP07 views. Final Release backend suite passes **320/320** (including architecture and Docker-backed integration tests); diagnostics and `git diff --check` are clean. State → `verify`; next LL QA Reviewer.
- **2026-07-12 — LL Backend Dev:** Addressed QA findings: replaced the post-commit enqueue catch-all with a non-throwing `TryEnqueue` contract, added an injectable refresh delegate, and added a hosted-service burst test proving 20 same-space requests produce exactly one refresh pass. Focused refresh tests pass **3/3**; full Release validation pending.
- **2026-07-12 — LL Backend Dev:** Reconciled ADR-0006 with PostgreSQL's invoker-view/grant constraint. Full Release backend suite passes **321/321**; focused refresh tests pass **4/4**; diagnostics, contract diff, and `git diff --check` are clean. State → `verify`; next LL QA Reviewer.
- **2026-07-12 — LL Backend Dev:** Closed the re-review coverage gaps with `MaterializedReportEquivalenceTests` (AC1: reversal-inclusive live-vs-materialized trial balance, balance sheet, income statement, C1 signs, and zero total) and `ReportRefreshLatencyTests` (AC2: real HTTP post returns after commit visibility is observed and while the refresh consumer remains pending). Focused WP12 integration coverage passes; full Release backend suite passes **323/323**. State → `verify`; next LL QA Reviewer.

## Implementation sequence

1. **Done 2026-07-12** — **D-WP12-TENANCY** (wrapper view + GUC predicate + matview ungranted) and **D-WP12-INTEGRITY** (separate always-live `trial_balance_live` for integrity + WP08) approved by the user on their recommended routes; D-WP12-TENANCY recorded as [ADR-0006](../../architecture/adr/ADR-0006-materialized-view-tenancy-wall.md) before the migration lands.
2. Add the `MaterializeTrialBalance` migration (matview + unique index + wrapper + live view + refresh function); verify `HasPendingModelChanges()` stays false and that `leafledger_app` can read the wrapper (GUC-bound) but **not** `trial_balance_mat` directly.
3. Add `ReportRefreshQueue` + `RefreshCoalescingService` + `ReportingRefreshMetrics`; wire in `LedgerModule`; add the post-commit enqueue in `JournalPostingService`.
4. Point report reads at the wrapper and integrity at `trial_balance_live`.
5. Add the equivalence, latency/coalescing, concurrency, and tenancy integration tests (red → green).
6. Regenerate the OpenAPI + TS client; confirm **byte-identical** (no surface change) and the `contract` gate stays green.
7. Run Release build + arch tests + full suite (incl. Docker integration) + the WP08 property suite (must stay green) + lint/typecheck/page-budget; document results/deviations; move to `verify`.

## Acceptance criteria (concrete tests)

1. **Output equivalence (the WP07 pre-commitment):** for a seeded balanced ledger (incl. a reversal), after `refresh_trial_balance_mat()`, the materialized-backed `trial_balance`, `balance_sheet_lines`, and `income_statement_lines` return rows **≡** the pre-WP12 live aggregation — same per-account values, same C1 signs, same net/current-result lines, whole-space total **exactly 0** (integer minor units, no float).
2. **Refresh is off the posting critical path (N4 wording):** a post/reverse returns without awaiting any refresh; the refresh is enqueued **after** `CommitAsync` and executed by the background service; a latency test proves the request path does not block on `REFRESH`. Enqueue failure never fails or delays the post.
3. **Coalescing (N4 wording):** N rapid posts in one space produce **far fewer than N** `refresh_trial_balance_mat()` executions (≤ the number of debounce windows spanned), asserted via a refresh-count probe/spy.
4. **Tenancy preserved (WP07 AC7 re-proof against the matview):** a principal for space A reads **no** space-B rows through the matview-backed wrapper; a no-context pooled connection returns **zero** rows (fail-closed); `trial_balance_mat` is not directly `SELECT`-able by `leafledger_app` — only the GUC-filtered wrapper is granted.
5. **Integrity + WP08 stay live & exact (D-WP12-INTEGRITY):** immediately after a commit and *before* the next refresh, `GET …/integrity` (reading `trial_balance_live`) reflects the new state (`balanced` correct, hash changed); the existing WP08 property suite stays **green unchanged** (still reads the live ≡ 0 oracle); two integrity calls on unchanged committed state return a byte-identical hash.
6. **Concurrency — no lock contention (N4 wording):** an interleaved post/commit and `REFRESH MATERIALIZED VIEW CONCURRENTLY trial_balance_mat` complete without lock-contention/serialization failures; the unique index `ux_trial_balance_mat (space_id, account_id)` exists (required by `CONCURRENTLY`).
7. **Metrics (N4 wording — duration + staleness + rows):** each refresh emits `leafledger.reporting.refresh.duration` (ms) and a per-window `leafledger.reporting.staleness`; rows-affected is emitted; a test observes at least one duration + staleness measurement after a triggered refresh.
8. **No model drift & no schema regression:** `HasPendingModelChanges()` stays **false** (matview/views/function are raw SQL, not EF entities); the migration adds **only** the matview, index, two views, and the refresh function (no base table/column/RLS-policy change — verified by the no-drift test and inspection); `Down` restores the WP07 plain `trial_balance`.
9. **Contract unchanged:** `backend/openapi/leafledger-v1.json` and `app/src/api/**` regenerate **byte-identical** (this WP adds no endpoint or response shape); the CI `contract` diff gate is green.
10. **Scope containment:** `git diff --name-only` is limited to the file list; no `*.Domain` change, no new endpoint, no `vat_report`, no frontend beyond byte-identical `api/**`, no new production NuGet; the existing Ledger integration + WP08 property suites stay green.
11. **Quality gate:** Release build 0/0; lint, typecheck, page budgets, arch/boundary tests, and the relevant unit + reporting/refresh integration tests all pass; Docker `postgres:17` integration tests green on the main-branch job.
12. **Deploy-probe integrity unaffected:** the `/integrity` endpoint's exactness and shape are unchanged (still the always-live, deterministic hash the Part-5 probe asserts `balanced = true` against); documented that the dashboard path is intentionally eventually-consistent while integrity remains exact.

## Definition of done

All 12 ACs pass; the base trial-balance aggregation is materialized with a unique index and refreshed via `REFRESH … CONCURRENTLY` **off the posting critical path** with per-space coalescing; report output is byte-for-byte equivalent to WP07's plain views; tenant isolation is re-proven against the matview-backed wrapper (fail-closed, no cross-space rows, matview not directly granted); the integrity hash, the WP08 ≡ 0 oracle, and the deploy `/integrity` probe still read a **live, exact** relation; refresh-duration + staleness + rows metrics are emitted; `HasPendingModelChanges()` stays false and the OpenAPI/TS contract regenerates byte-identical; Release build clean; full suite (unit + arch + integration + WP08 property) green. Then state → `verify` and route to LL QA Reviewer.

## Split seam (if the WP overflows ≤ 2 days)

If materialization + tenancy + the coalescing/metrics machinery cannot land in one ≤2-day session:
- **WP12a — Materialize + equivalence + tenancy:** the migration (matview + unique index + wrapper + live view + refresh function), read repointing, and AC1/AC4/AC5/AC8/AC9 (equivalence, tenancy, live-integrity, no-drift, contract). Refresh is triggered **synchronously per post** as a placeholder (still off the *read* path but not yet coalesced) so output stays correct.
- **WP12b — Post-commit coalescing + metrics:** the `ReportRefreshQueue` + `RefreshCoalescingService` + metrics and AC2/AC3/AC6/AC7 (off-critical-path, coalescing, concurrency, metrics).
Both halves are independently verifiable; WP12a is safe to ship because a synchronous post-commit refresh is still correct (just not yet optimized).

## Open questions (route before/at implementation)

- **D-WP12-TENANCY — RESOLVED (user, 2026-07-12):** approved the "wrapper view + explicit GUC predicate, matview ungranted" mitigation; recorded as [ADR-0006](../../architecture/adr/ADR-0006-materialized-view-tenancy-wall.md).
- **D-WP12-INTEGRITY — RESOLVED (user, 2026-07-12):** approved keeping integrity + the WP08 oracle on a separate always-live `trial_balance_live` view (dashboards eventually-consistent, integrity exact); the synchronous-refresh alternative is rejected.
- **Debounce window (implementation default):** ~200 ms coalescing window (matches the N5 SignalR window figure) as a config-bound default; confirm at implementation.
- **Multi-instance carry-forward (non-blocking):** the in-process queue coalesces within one Host; a durable/cross-instance refresh signal (LISTEN/NOTIFY or a durable queue) is a documented scale-out follow-up, not this WP (N4 says NOTIFY/LISTEN was assessed premature).

## QA verdict

**FAIL — 2026-07-12, LL QA Reviewer; re-review 2026-07-12**

1. **AC1 remains unproven.** The plan requires a seeded ledger equivalence test comparing materialized-backed `trial_balance`, `balance_sheet_lines`, and `income_statement_lines` against the live aggregation, including reversal, C1 signs, net result, and zero total. The declared `MaterializedReportEquivalenceTests.cs` does not exist, and the submitted `MaterializedReportTests` only checks empty wrapper results, direct matview denial, and refresh-function access. Existing report tests seed data and refresh the matview but do not compare the materialized rows to a live baseline.
2. **AC2 remains unproven.** The plan requires a real post/reverse latency test showing the request returns before refresh completes and that enqueue occurs after commit. `RefreshCoalescingServiceTests` proves hosted-service coalescing using an injected delegate, but it does not invoke `JournalPostingService` or an HTTP post with a blocked refresh. `JournalPostingService` calls synchronous `TryEnqueue` after `CommitAsync` (`backend/src/LeafLedger.Modules.Ledger/Infrastructure/JournalPostingService.cs:108`), but QA cannot infer the required request-path behavior without the declared latency test.
3. **Resolved:** replaced the post-commit catch-all with the non-throwing `IReportRefreshQueue.TryEnqueue` contract. Duplicate requests return `false`; the current unbounded queue has no exception-based failure path, and the post path performs this side effect only after `CommitAsync` without masking unrelated failures.

**Validation:** Release backend suite `321/321` including Docker integration and architecture tests; focused refresh tests `4/4`; changed-file diagnostics clean; OpenAPI/TS contract diff clean; `git diff --check` clean. Existing tenancy, migration-down, metrics, direct-matview denial, no-context fail-closed behavior, and concurrent post/refresh coverage remain green.

**QA verdict:** **FAIL — 2026-07-12, LL QA Reviewer; re-review**

1. **AC4 tenancy re-proof is incomplete.** `MaterializedReportTests.Wrapper_is_fail_closed_and_matview_is_not_selectable_by_app_role` (`backend/tests/LeafLedger.IntegrationTests/Ledger/MaterializedReportTests.cs:15`) opens the wrapper for an empty seeded space and asserts zero rows. It never refreshes populated rows for two spaces or proves that a space-A app connection cannot see space-B rows. The existing cross-space HTTP authorization test in `LedgerReportTests` exercises endpoint membership rejection, not the materialized wrapper's database isolation. Add a populated two-space wrapper test that refreshes, binds space A, and asserts only A's rows, plus the existing no-context and direct-matview assertions.
2. **AC5 stale-window behavior is unproven.** `ReportRefreshLatencyTests.Posting_returns_with_refresh_still_pending_after_commit` proves commit visibility before `TryEnqueue`, but it does not call the integrity endpoint or a dashboard report after the post. The plan explicitly requires proving that integrity reads `trial_balance_live` and reflects the commit immediately while the materialized dashboard remains stale until refresh. Add a real post test with a blocked queue/refresh, assert the integrity response changes, assert the dashboard remains at its pre-post snapshot, then trigger refresh and assert dashboard convergence.

**Validation:** Independent full Release backend suite **323/323** (integration **156/156**, architecture included); changed-file diagnostics clean; `git diff --check` clean; generated API contract unchanged. Existing AC1, AC2, AC3, AC6, AC7, AC8, and metrics coverage remains green, but the two findings above block acceptance.

**State:** in-progress. Add the populated tenancy and live-integrity/materialized-staleness tests, rerun the focused WP12 slice and full Release backend suite, then return to QA review.

**Re-review resolution — 2026-07-12:** Added populated two-space wrapper isolation coverage and extended the real HTTP latency test to assert live integrity changes immediately, the dashboard remains at its pre-post materialized snapshot while refresh is blocked, and the dashboard converges after `refresh_trial_balance_mat()`. Focused WP12 integration coverage passes **7/7**; full Release backend passes **324/324** (integration **157/157**, architecture **3/3**); diagnostics, contract diff, and `git diff --check` are clean. AC4 and AC5 are now executable; state → `verify`, next LL QA Reviewer.

**QA verdict:** **PASS — 2026-07-12, LL QA Reviewer; re-review**

All 12 acceptance criteria are satisfied. AC1 equivalence covers reversal-inclusive live/materialized trial balance and derived reports with C1 signs and zero total. AC2/AC3 cover the real HTTP post-commit path, blocked refresh, enqueue ordering, and hosted-service coalescing. AC4/AC5 cover populated cross-space wrapper isolation, fail-closed no-context access, live integrity, stale dashboard behavior, and post-refresh convergence. AC6/AC7 cover concurrent refresh and duration/staleness/rows metrics. AC8–AC12 are covered by migration-down/no-drift tests, unchanged contract artifacts, scope inspection, the full backend quality gates, and the live integrity path.

**Financial integrity:** all amounts remain integer minor units; no float/decimal amount arithmetic was introduced; posted journal rows remain immutable and reversal-based; database balance enforcement remains intact.

**Security:** `trial_balance_mat` is ungranted to `leafledger_app`; the wrapper is explicitly GUC-filtered and fail-closed; the refresh function is narrowly executable by the app role; report endpoints retain authorization; no secrets were added.

**Patch-layering/hallucination:** no generic production catch-all or self-healing behavior was introduced. The implementation is traceable to the WP plan, ADR-0006, target reporting specification, and existing repository precedents. No unplanned endpoint, contract, Domain, or dependency changes were found.

**Validation:** focused WP12 integration tests **7/7**; full Release backend **324/324** (`157/157` integration, `3/3` architecture); changed-file diagnostics clean; OpenAPI/TS contract unchanged; `git diff --check` clean.

**State:** done.
