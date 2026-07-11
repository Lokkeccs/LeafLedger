# P2-WP07 — Reporting: trial-balance / balance-sheet / income-statement SQL views + `/integrity` hash

- **Phase:** 2 (ledger core)
- **State:** verify — implementation and local acceptance validation completed on 2026-07-11.
- **Owner (implementation):** LL Backend Dev
- **Depends on:** P2-WP02 (schema: `journal_entries`/`journal_lines`/`accounts` with `base_amount_minor`, `accounts.kind`, RLS `leafledger_app` role + `app.current_space_id` GUC), P2-WP05 (posted entries + txn-local read/write binding pattern), P2-WP06 (authorization endpoint filter + principal-bound RLS binding this WP reuses for the read endpoints).
- **Blocks:** P2-WP08 (financial-invariant property suite reads the trial-balance view / integrity hash as its ≡0 oracle), the deploy `/integrity` probe (Part 5 §pipeline: `/health` + `/integrity` before slot swap).
- **Estimated size:** ≤ 2 days (three read-only SQL views in one migration + a read-binding report service + `GET reports/*` and `GET integrity` endpoints + a `ledger.read` permission + integrity-hash canonicalization + per-endpoint integration/RLS tests + contract regen).

## Re-scope note (LL Architect, 2026-07-11)

The Phase-2 tracker row for WP07 read *"Reporting: trial-balance / balance-sheet / income-statement SQL views + `/integrity` hash; mat-view refresh strategy (**N4**)."* Delivered literally that bundles **two independently-verifiable concerns of different risk classes**:

1. **Correctness** — the report views + integrity hash must be *exactly right* (they are the WP08 invariant oracle and the deploy gate). This is the accounting-critical half.
2. **Performance (N4)** — promoting hot views to **materialized** views with `REFRESH … CONCURRENTLY`, post-commit enqueue + coalescing off the posting critical path, and refresh-duration/staleness metrics. This is a substantial infrastructure half (background job runner, coalescing window, unique indexes, concurrency tests) that only earns its keep under load and is a pure optimization of an already-correct result.

Bundled, they exceed the ≤ 2-day / independently-verifiable rule. **WP07 is therefore scoped to the correctness half:** the three reports as **plain (non-materialized) `security_invoker` SQL views** — always live, always consistent with the committed ledger, no refresh machinery, no staleness window — plus the integrity hash and the read endpoints. The N4 materialization strategy is carved out into **P2-WP12 (new, proposed)**; folding N4 there (not here) is recorded in status.md in the same change. Plain views are the correct starting point: they are *by construction* never stale, so the integrity hash and WP08 property suite have a trustworthy oracle before any caching is introduced. WP12 later promotes only the views proven hot, preserving identical output (a WP12 acceptance criterion will be "materialized view output ≡ plain view output").

- **Spec sources:** `docs/architecture/rebuild/03-target-architecture.md` §4.3 (**Reporting**: "`trial_balance`, `balance_sheet_lines`, `income_statement_lines`, `vat_report` as SQL views (materialized where hot, refreshed on posting). `GET /spaces/{id}/integrity` returns a trial-balance hash — still the convergence/monitoring primitive, now trivially computed."), §4.2 (schema: `journal_lines.base_amount_minor BIGINT` signed +debit/−credit, `journal_entries.status`/`date`, `reverses_entry_id`), §1/§72 ("cross-entity SQL reporting (trial balance as a view)" and "server-computed dashboards (no derived-state drift)" listed as **new capabilities the old stack couldn't offer** → spec-derived rewrite, not a port), §6 (API: `GET /api/v1/spaces/{spaceId}/reports/trial-balance|balance-sheet|income-statement|vat` and `GET /api/v1/spaces/{spaceId}/integrity`), §8.3 (RLS second wall), §9 (Result → ProblemDetails); `docs/architecture/rebuild/04-implementation-plan.md` §Phase 2 (exit criterion: "any generated command sequence yields **trial balance ≡ 0**") + §5 (rewrite items are spec-derived, pinned by tests); `docs/architecture/rebuild/05-quality-and-maintainability.md` §pipeline (`/health` + **`/integrity` probe** before slot swap) + §testing (trial-balance ≡ 0 is the flagship invariant); `docs/architecture/rebuild/06-feature-roadmap.md` M1 (Trial balance, balance sheet, P&L — server-computed); `docs/rebuild/plans/NOTES-risk-review-2026-07-06.md` **N4** (folded into P2-WP12, not this WP); ADR-0001 (server/DB are the source of truth); ADR-0002 (uuid ids).

## Goal

Expose the ledger's derived financial position as **server-computed, database-defined SQL views** and a **deterministic integrity hash**, reading them through the authenticated, RLS-bound pipeline established by WP05/WP06:

- `GET /api/v1/spaces/{spaceId}/reports/trial-balance` — per-account net base-currency balance over posted entries; the whole-space total is the ≡ 0 invariant.
- `GET /api/v1/spaces/{spaceId}/reports/balance-sheet` — Asset/Liability/Equity accounts, classified by `accounts.kind`, presented per the C1 sign convention.
- `GET /api/v1/spaces/{spaceId}/reports/income-statement` — Income/Expense accounts, classified by `accounts.kind`, presented per the C1 sign convention.
- `GET /api/v1/spaces/{spaceId}/integrity` — a deterministic hash of the trial balance + a `balanced` flag, the convergence/monitoring primitive and the deploy probe.

All four are **read-only**, require `ledger.read` (every space member, including `Viewer`), run under `leafledger_app` with the route `{spaceId}` bound **txn-locally**, and rely on RLS as the second wall so a valid principal for space A can never read space B. `vat_report` is explicitly **out of scope** (Phase 4 / M1-second-half).

## Scope

1. **Reporting migration (new EF migration, raw SQL views):**
   - A new migration `ReportingViews` creating three views via `migrationBuilder.Sql`, each declared `WITH (security_invoker = true)` (PostgreSQL 15+; the schema runs `postgres:17`) so RLS on the underlying tables is evaluated as the **querying** `leafledger_app` role — not the view owner — preserving the space-isolation second wall.
     - `trial_balance(space_id, account_id, account_code, account_name, account_kind, base_balance_minor)` — `SUM(jl.base_amount_minor)` grouped by account over `journal_lines jl JOIN journal_entries je … WHERE je.status = 'posted'`; every posted line (including reversals, which carry negated `base_amount_minor`) participates, so the grouped total per space is exactly 0.
     - `balance_sheet_lines(space_id, account_id, account_code, account_name, account_kind, amount_minor)` — the `trial_balance` rows where `account_kind IN ('Asset','Liability','Equity')`, sign presented per **C1**.
     - `income_statement_lines(space_id, account_id, account_code, account_name, account_kind, amount_minor)` — the `trial_balance` rows where `account_kind IN ('Income','Expense')`, sign presented per **C1**.
   - `GRANT SELECT` on the three views to `leafledger_app`; no writes; no `SECURITY DEFINER`. The migration adds **only** views + grants — no table/column/trigger/RLS-policy change (the arch `HasPendingModelChanges()` no-drift test must stay green: views are raw-SQL, not EF-modelled entities).
2. **Report read service (Ledger Application + Infrastructure):**
   - `ILedgerReportService` (public contract, Application) with `GetTrialBalanceAsync`, `GetBalanceSheetAsync`, `GetIncomeStatementAsync`, `GetIntegrityAsync(spaceId, ct)`; DTOs in `Application/Reporting/`.
   - Implementation in `Infrastructure` reads the views under a **txn-local read binding** (`SET LOCAL ROLE leafledger_app` + `set_config('app.current_space_id', spaceId, is_local => true)`), reusing the WP05/WP06 binding helper so no space context leaks into a pooled connection. EF stays confined to `Infrastructure`.
3. **Integrity hash (Infrastructure, deterministic):**
   - Compute over the `trial_balance` rows for the space, **canonically ordered by `account_code` ascending** (tie-break `account_id`), each row serialized as a fixed, culture-invariant string of integer minor units (e.g. `"{account_code}:{base_balance_minor}"`) joined by `\n`, prefixed with a version tag + `space_id`; hash = **SHA-256**, returned lowercase hex. No floats, no locale formatting, no timestamps inside the hashed payload (the response timestamp, if any, lives outside the hash).
   - Response: `{ spaceId, algorithm: "sha256", version, lineCount, trialBalanceHash, balanced }` where `balanced = (SUM(base_balance_minor) == 0)`. Deterministic: the same committed ledger state → byte-identical `trialBalanceHash` across calls, processes, and OSes.
4. **Read endpoints (Host / Ledger `Infrastructure` endpoint map):**
   - `GET …/reports/trial-balance`, `…/reports/balance-sheet`, `…/reports/income-statement`, `…/integrity`, each `.Produces` a typed 200 body + 401/403 ProblemDetails, and each guarded by `RequireSpacePermission("ledger.read")`.
5. **`ledger.read` permission (Host, cross-cutting):**
   - Add `ModulePermissions.ReadLedger = "ledger.read"` and grant it to **all** roles (`Owner`/`Admin`/`Member`/`Viewer`) — reading reports is the one thing a `Viewer` may do. `Owner`/`Admin` already receive every known permission; extend `Member` and `Viewer` to include `ledger.read`. Unknown/blank role still denies.
6. **Contract + tests:** regenerate `backend/openapi/leafledger-v1.json` + the TS client (four new `GET` operations + their response schemas + 401/403); the CI `contract` diff gate must stay green. Add report/integrity/RLS/authorization integration tests + an integrity-hash determinism unit test.

## Non-goals (explicitly deferred)

- **No materialized views / no N4 refresh machinery — P2-WP12.** No `MATERIALIZED VIEW`, no `REFRESH … CONCURRENTLY`, no post-commit enqueue/coalescing, no background refresh job, no refresh-duration/staleness metrics, no unique-index-for-concurrent-refresh. WP07 ships plain, always-consistent views; WP12 promotes the hot ones with identical output.
- **No `vat_report`.** VAT reporting is M1-second-half (Phase 4); the migration creates only the three non-VAT views.
- **No new write behavior / no posting change.** WP04 domain and WP05 orchestration are untouched; this WP only reads.
- **No frontend report pages / dashboards.** Server-computed reports only; the React report/dashboard UI is Phase 3 (app shell) / M1 UI. Only regenerated `app/src/api/**` changes.
- **No pagination/filtering/drill-down beyond a whole-space snapshot.** Cursor pagination, date-range/account filters, account drill, and period-scoped reports are follow-ups (M1/M2); WP07 returns the current whole-space position (all posted entries). A `date`/period query parameter is a plan amendment, not ad-hoc scope.
- **No new business tables or columns.** Views + grants only; if a genuinely missing column surfaces it is a plan amendment, not an ad-hoc schema change.
- **No account-classification rework.** Classification uses the existing `accounts.kind` (Asset/Liability/Equity/Income/Expense) from P2-WP03 verbatim; WP07 does not redefine account types or group semantics.
- **No authentication change.** Reuses WP06's JWT-bearer + test-scheme seam and principal-bound binding as-is; real Entra is still P2-WP11.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

### Reference only (not salvaged as code) — "new capability" per spec §72
- OLD reporting (trial balance, balance sheet, P&L, integrity/convergence checksum) executed **entirely client-side** over Dexie/IndexedDB inside React report components and the sync layer (`src/data/db.ts` carries derived `baseCurrencyDebitTotal`/`baseCurrencyBalance` cache columns; the reports themselves are UI-computed). A GitHub code search for an executable, capturable trial-balance / balance-sheet / income-statement / integrity-hash function found **none** — the logic is entangled with rendering and Dexie queries, not a pure unit. Spec §72 lists "cross-entity SQL reporting (trial balance as a view)" and "server-computed dashboards (no derived-state drift)" as **capabilities the old stack could not offer**. WP07 is therefore a **spec-derived rewrite**, not a port: SQL views replacing UI computation. The OLD `baseCurrencyBalance` cache column is *not* ported (it is the derived-state drift the rewrite eliminates).

### Rewrite (spec-derived; no OLD oracle)
- The three SQL views, the integrity-hash canonicalization, the read service, and the `ledger.read` gate are greenfield per §4.3/§6. They are pinned by **integration tests + a determinism unit test + the WP08 property invariant (trial balance ≡ 0)** — the correct instruments for server-computed reporting, exactly as WP06 used per-endpoint tests for access control.

## Accounting decisions

- **C1 (resolved 2026-07-11 by LL Accounting Expert):** the **presentation sign convention** for `balance_sheet_lines` and `income_statement_lines`, and the composition of the net-result line.
   - `trial_balance.base_balance_minor` remains the raw signed net (`+` = net debit, `-` = net credit) with a whole-space total of exactly 0. This is the invariant source and is not presentation-adjusted.
   - `balance_sheet_lines` presents Assets as `base_balance_minor`; Liabilities and Equity as `-base_balance_minor`, so normal credit balances display as positive magnitudes. The current-period result is surfaced once as a distinct `current_result` equity line (or equivalent equity component), and must not be double-counted in the equity total.
   - `income_statement_lines` presents Income as `-base_balance_minor` and Expenses as `base_balance_minor`, so normal revenue and cost amounts display positively. It includes one derived `net_result` line, calculated as `total_income - total_expenses`; positive means profit and negative means loss. The derived result is not an additional journal-account row.
   - This is a presentation convention consistent with the magnitude-oriented structure of OR Arts. 959, 959a and 959b; those provisions do not require the database's internal debit-positive sign convention. The decision is target-derived, not an OLD behavior port.
   - **No golden fixtures required** (the OLD reporting was UI-computed with no capturable pure oracle). C1 is pinned by explicit-value integration tests over a seeded ledger, including per-line signs, current-result treatment, and net-result calculation.

The trial-balance ≡ 0 invariant and the integrity-hash canonicalization are **not** accounting decisions (mechanical aggregation + deterministic serialization) and need no consult.

## Golden fixtures

**None required.** WP07 ports no OLD accounting function (the OLD reporting was UI-computed with no capturable oracle — see Source material). Report correctness is pinned by:
- explicit-value **integration tests** over a small seeded ledger (known postings → asserted trial-balance rows, balance-sheet/income-statement lines, and total ≡ 0),
- an **integrity-hash determinism** unit test (same state → identical hash; one changed minor unit → different hash; ordering-independent input → identical hash),
- and the **WP08 property invariant** (any generated posting sequence ⇒ trial-balance total 0 ⇒ `integrity.balanced = true`).
This mirrors the WP06 precedent: a spec-derived rewrite is pinned by the testing-pyramid tier appropriate to it, not by golden fixtures. (Recorded explicitly so QA does not expect a golden artifact.)

## Dependencies

- **No new production NuGet.** SHA-256 is `System.Security.Cryptography` (BCL); views are raw SQL via `MigrationBuilder`; reads use the existing Npgsql/EF stack. Test project already has `Microsoft.AspNetCore.Mvc.Testing`/`Microsoft.AspNetCore.TestHost` + the Docker `postgres:17` fixture (WP05/WP06).
- The P1-WP04 OpenAPI build-time doc + `openapi-typescript`/`openapi-fetch` pipeline regenerates the contract (four GET operations); the CI `contract` diff gate must stay green.

## File list (implementation target)

**New — `backend/src/LeafLedger.Modules.Ledger/Infrastructure/Migrations/`**
- `<timestamp>_ReportingViews.cs` (+ generated `.Designer.cs` / snapshot delta **only if EF emits one — it must not**, since views are raw SQL): `CREATE VIEW … WITH (security_invoker = true)` for `trial_balance`, `balance_sheet_lines`, `income_statement_lines` + `GRANT SELECT … TO leafledger_app`; `Down` drops them. No table/column/trigger/policy change.

**New — `backend/src/LeafLedger.Modules.Ledger/Application/Reporting/`**
- `ILedgerReportService.cs` (public contract) + report/integrity DTOs (`TrialBalanceRow`, `TrialBalanceReport`, `BalanceSheetReport`, `IncomeStatementReport`, `IntegrityReport`).

**New — `backend/src/LeafLedger.Modules.Ledger/Infrastructure/`**
- `LedgerReportService.cs` (txn-local read binding + view reads via EF/`FromSql`/keyless entities or ADO reader) and `IntegrityHasher.cs` (deterministic canonical SHA-256 over the trial balance).
- Report endpoint wiring: extend `LedgerEndpoints.cs` (or a sibling `LedgerReportEndpoints.cs`) with the four `GET` routes + `RequireSpacePermission("ledger.read")`.

**Modified**
- `backend/src/LeafLedger.Host/Authorization/ModulePermissions.cs` — add `ReadLedger = "ledger.read"`; grant to all four roles (Member/Viewer gain read; Owner/Admin already all).
- `backend/src/LeafLedger.Host/Program.cs` — register `ILedgerReportService`; map the four read endpoints with the `ledger.read` filter + auth-failure ProblemDetails (reuse existing wiring).
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerModule.cs` — register `ILedgerReportService`; register keyless view entity types on `LedgerDbContext` **as `ToView(...)`/`HasNoKey()` mappings that produce no migration** (or read via raw SQL to avoid model additions — chosen to keep `HasPendingModelChanges()` green).
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerDbContext.cs` — only if keyless view mappings are used (must not add a pending model change vs the schema; verify with the no-drift test).
- `backend/openapi/leafledger-v1.json` — regenerated (four GET operations + response schemas + 401/403).
- `app/src/api/schema.d.ts` (+ `client.ts` if the generator changes it) — regenerated; **do not hand-edit**.
- `backend/tests/LeafLedger.ArchitectureTests/ModuleBoundaryTests.cs` — only if a new namespace needs coverage; existing EF-confinement + Domain-purity rules stay green unchanged.
- `docs/rebuild/plans/P2-WP07-reporting-views-integrity.md` + `docs/rebuild/status.md` — notes/state.

**New — tests**
- `backend/tests/LeafLedger.IntegrationTests/Ledger/LedgerReportTests.cs` (`[Trait("Category","Integration")]`) — real HTTP + RLS on `postgres:17`: seed a small balanced ledger (incl. a reversal) → trial-balance rows match expected per-account net + total ≡ 0; balance-sheet lines cover only Asset/Liability/Equity with the C1 sign; income-statement lines cover only Income/Expense with the C1 sign and the net-result line; `integrity.balanced = true` and `trialBalanceHash` stable across two calls; a directly-crafted unbalanced state (bypassing the app) is reflected as `balanced = false` (the hash still computes).
- `backend/tests/LeafLedger.IntegrationTests/Ledger/LedgerReportAuthorizationTests.cs` (`[Trait("Category","Integration")]`) — unauthenticated → 401; non-member → 403 `auth.not_a_member`; `Viewer` → **200** on all four read endpoints (read is allowed); a principal for space A cannot read space B's reports/integrity (403 at the filter **and** RLS second wall yields no cross-space rows even if the filter were bypassed — mirroring WP02 `OpenAppNoContextAsync` fail-closed).
- `backend/tests/LeafLedger.Modules.Ledger.Tests/IntegrityHasherTests.cs` (unit) — determinism: identical trial-balance input → identical hash; a single changed minor unit → different hash; input row order does not affect the hash (canonical ordering); empty ledger → a stable, documented hash; culture-invariant (no locale leakage).

No files under `app/src/**` except regenerated `api/**`; no `*.Domain` change; no `MATERIALIZED VIEW`; no `vat_report`; no frontend pages.

## Boundary note

- Report DTOs + `ILedgerReportService` live in the Ledger module's **Application** (public contract) surface; the SQL/EF read lives in **Infrastructure**; the endpoints + permission live in the Host per §5. The arch tests `EfCoreIsConfinedToInfrastructureNamespaces` and `DomainNamespacesDependOnlyOnSharedKernel` must stay green.
- Views are defined in the Ledger migration (the module owns its schema per D1 single-context baseline). A future extraction into a `LeafLedger.Modules.Budgets`/Reporting module (spec §3) is a **carry-forward**, not this WP.
- The read binding is txn-local (`is_local => true`) exactly like the WP05/WP06 write/membership binding — no space context may leak into a pooled connection.

## Implementation sequence

1. Confirm **C1** with LL Accounting Expert (presentation sign convention + net-result line); record the exact rule in this plan; get user approval. **Done 2026-07-11.**
2. Add the `ReportingViews` migration (three `security_invoker` views + grants); verify `HasPendingModelChanges()` stays false (raw SQL, no model drift) and the views return rows under `leafledger_app` with a bound space (RLS applies as invoker). **Done 2026-07-11; migration and no-drift integration checks green.**
3. Add `IntegrityHasher` + its determinism unit test (red → green) before wiring the endpoint. **Done 2026-07-11; focused tests 4/4 green.**
4. Add `ILedgerReportService` + `LedgerReportService` (txn-local read binding + view reads) + DTOs; register in `LedgerModule`. **Done 2026-07-11; report integration checks green.**
5. Add `ledger.read` to `ModulePermissions` (all roles) + its map unit assertion; wire the four `GET` endpoints in the Host with `RequireSpacePermission("ledger.read")`; regenerate the OpenAPI contract + TS client. **Done 2026-07-11.**
6. Add `LedgerReportTests` + `LedgerReportAuthorizationTests` (reports, integrity, RLS second wall, 401/403/Viewer-200). **Done 2026-07-11 in `LedgerReportTests`; five tests cover the matrix.**
7. Run Release build + arch tests + full suite (incl. integration under Docker) + lint/typecheck/page-budget + contract gate; document results/deviations; move to `verify`. **Done locally 2026-07-11; WP state moved to `verify`.**

## Acceptance criteria (concrete tests)

1. **Build, boundaries & contract:** `dotnet build LeafLedger.sln -c Release` = 0/0; architecture tests stay green (`*.Domain` → SharedKernel only; EF confined to `Infrastructure`); `HasPendingModelChanges()` remains **false** after the views migration (views are raw SQL, not EF-modelled entities); the CI `contract` gate is green after regenerating `leafledger-v1.json` + the TS client (four GET operations + 401/403 added, no unintended drift).
2. **Trial balance correctness + ≡ 0:** over a seeded balanced ledger (including at least one reversal), `GET …/reports/trial-balance` returns one row per account with `base_balance_minor` = `SUM(base_amount_minor)` of that account's posted lines, and the whole-space total is **exactly 0** (integer minor units, no float). Draft/non-posted entries (if any) are excluded (`status = 'posted'`).
3. **Balance-sheet classification + sign:** `GET …/reports/balance-sheet` returns exactly the Asset/Liability/Equity accounts (by `accounts.kind`), no Income/Expense rows, with amounts presented per the **C1** convention (asserted against explicit expected values on the seeded ledger).
4. **Income-statement classification + sign:** `GET …/reports/income-statement` returns exactly the Income/Expense accounts (by `accounts.kind`), no balance-sheet rows, with amounts + net-result per the **C1** convention (asserted against explicit expected values).
5. **Integrity hash — determinism & balanced flag:** `GET …/integrity` returns `{ algorithm:"sha256", trialBalanceHash, balanced, lineCount, version }`; two calls on unchanged committed state return a **byte-identical** `trialBalanceHash`; `balanced = true` iff the trial-balance total is 0; a directly-crafted unbalanced state yields `balanced = false` while the hash still computes. Hash is culture-invariant and order-independent (unit test: reordered input → identical hash; one changed minor unit → different hash; empty ledger → documented stable hash).
6. **Authorization — read allowed for all members:** unauthenticated → **401** `auth.unauthenticated`; authenticated non-member → **403** `auth.not_a_member`; a `Viewer` gets **200** on all four read endpoints (read is the Viewer-permitted action); the `ledger.read` permission maps to all four roles (unit assertion) and unknown/blank role still denies.
7. **RLS second wall on reads:** a principal that is `Owner`/`Member`/`Viewer` of space A cannot read space B's reports or integrity — 403 at the filter; and even a crafted read that reached the view with no/other space context returns **no** cross-space rows (space-bound policy on the underlying tables, evaluated as the invoker because the views are `security_invoker = true`) — mirroring the WP02 `OpenAppNoContextAsync` fail-closed guarantee; a pooled connection with no context remains fail-closed.
8. **Read binding is txn-local:** the report/integrity read binds `app.current_space_id` = the route space via `SET LOCAL` / `is_local => true`; no space context leaks to a subsequent pooled use of the same connection (verified as WP05/WP06 bindings are).
9. **Scope containment:** `git diff --name-only` is limited to the file list; the migration adds **only** views + grants (no table/column/trigger/RLS-policy change — verified by the no-drift test and by inspection); **no `MATERIALIZED VIEW`**, no `vat_report`, no `*.Domain` change, no frontend beyond regenerated `api/**`, no new write endpoint, no new NuGet; the existing Ledger integration suite stays green.
10. **ProblemDetails shape:** auth failures on the read endpoints return `application/problem+json` with the WP06 stable codes (`auth.unauthenticated` → 401; `auth.not_a_member`/`auth.permission_denied` → 403) and no principal PII / stack traces; a successful read returns a typed 200 JSON body matching the regenerated contract.
11. **Quality gate:** lint, typecheck, page budgets, arch/boundary tests, and the relevant unit + reporting/authorization integration tests all pass in Release; integration tests run under Docker (`postgres:17`) via the `Category=Integration` filter and are green on the main-branch job.
12. **Deploy-probe readiness:** the `/integrity` endpoint is shaped so a deploy probe (Part 5 pipeline) can call it and assert `balanced = true` (documented; the actual pipeline wiring is a deploy/ops task, not this WP's code — recorded as a carry-forward if not already wired).

## Definition of done

All 12 ACs pass; the three reports and the integrity hash are database-defined, RLS-bound, `ledger.read`-gated, and read through the WP06 authenticated pipeline; trial balance nets to exactly 0 in integer minor units and the integrity hash is deterministic and order-independent; RLS is proven the second wall on reads with `security_invoker` views; the C1 sign convention is fixed and test-pinned; the migration adds only views + grants with no model drift; no materialized views / no `vat_report` / no `Domain` / no frontend beyond regenerated `api/**`; the OpenAPI contract regenerates green; Release build clean; full suite (unit + arch + integration) green. Then state → `verify` and route to LL QA Reviewer. Materialized-view refresh (N4) is P2-WP12; `vat_report` is Phase 4; report/dashboard UI is Phase 3/M1.

## Risks / notes

- **`security_invoker` is load-bearing for tenancy.** A default (invoker=false) view would evaluate RLS as the view *owner*, potentially bypassing the space-bound policy — a cross-tenant leak. The views **must** be `WITH (security_invoker = true)` (PG15+, available on `postgres:17`) so RLS is enforced as the querying `leafledger_app` role. AC7 tests this directly; do not relax it.
- **No model drift from views.** EF must not treat the views as owned entities that generate migration diffs. Either map them keyless with `ToView(...)`/`HasNoKey()` (no table create) or read via raw SQL; AC1's `HasPendingModelChanges()` guard is the tripwire.
- **Plain views, not materialized — deliberate.** WP07's job is a *correct* oracle for WP08 and the deploy probe; plain views are never stale. Performance/materialization (N4) is P2-WP12 and must preserve identical output. Do not pre-optimize here.
- **C1 is the only accounting gate.** Trial balance + integrity need no consult; only the balance-sheet/income-statement *presentation* sign + net-result line do. Implementation is blocked on C1 + user approval; keep the raw signed trial balance untouched regardless of C1 so WP08's ≡ 0 oracle is convention-independent.
- **Reversals are already posted entries** carrying negated `base_amount_minor` (WP04/WP05), so they net in the views automatically — the trial balance stays 0 after a post+reverse pair. AC2 seeds a reversal to prove it.
- **`status` filter.** Views include only `journal_entries.status = 'posted'`. If a non-posted status is ever introduced (drafts), it is excluded by construction; confirm the current status vocabulary during implementation and pin the filter.
- **Whole-space snapshot only.** No date/period scoping in WP07; period-scoped and date-ranged reports (and drill-down) are M1/M2 follow-ups. A period filter would change the view signature and is a plan amendment.

## Implementation notes

- **2026-07-11 — verify:** Added the raw-SQL `ReportingViews` migration with PostgreSQL `security_invoker` views and `leafledger_app` grants; the hand-authored migration also requires the generated-style `Migration` metadata file for EF discovery. Added application report contracts, transaction-local Npgsql report reads, database-defined C1 presentation amounts, `ledger.read` permission mapping for all roles, four authorized GET routes, and OpenAPI bearer coverage for all space-scoped operations. Regenerated `backend/openapi/leafledger-v1.json` and `app/src/api/schema.d.ts`.
- **Validation:** IntegrityHasher focused tests 4/4; report integration tests 5/5; final solution-wide Release suite 236/236, including architecture, no-model-drift, and PostgreSQL integration checks; frontend lint, typecheck, tests 3/3, page budget, and production build green. `git diff --check` is clean. C1 signs, net/current result, stable hash, unbalanced hash probe, Viewer access, stable auth codes, cross-space denial, and no-context RLS are pinned.
- **2026-07-11 — QA fix:** Typed the `200` response metadata for all four report/integrity endpoints, regenerated the OpenAPI contract and TypeScript schema, and verified the contract now references `TrialBalanceReport`, `BalanceSheetReport`, `IncomeStatementReport`, and `IntegrityReport`. Focused validation: Host Release build, frontend typecheck/tests 3/3, and report integration tests 5/5 pass.
```
