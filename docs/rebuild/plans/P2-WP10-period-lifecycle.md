# P2-WP10 — Period-lifecycle commands (create / close / reopen / lock) + onboarding open-period bootstrap

- **Phase:** 2 (ledger core)
- **State:** done. **B2.1–B2.5 are resolved** and the transition/boundary golden fixtures are captured. Authorization, bootstrap, posting-boundary, gap, and closed/locked acceptance coverage are now present. QA PASS 2026-07-12. **Merged to `main` via PR #19 on 2026-07-12.**
- **Owner (implementation):** LL Backend Dev
- **Depends on:** P2-WP02 (schema: `periods` table + RLS tenant pattern + `btree_gist` + audit trigger + `postgres:17` fixture), P2-WP04 (`PeriodState`/`PeriodSnapshot`/`PeriodStateResolver` domain + ADR-0005), P2-WP05 (the posting guard that *reads* periods + the txn-local binding pattern), P2-WP06 (authorization filter + `period.close` permission constant already declared), P2-WP09 (idempotency seam these write endpoints reuse). **First task depends on:** P2-WP01 harness conventions (LL Fixture Smith reuses them for the new transition fixtures).
- **Blocks:** **P2-WP08** invariants **I3** (closed/locked/undefined-period posts rejected — nothing can *create* a closed/locked period until this WP) and **I7** (period-boundary inclusivity — this WP owns the canonical `[start, end_exclusive)` reconciliation).
- **Estimated size:** ≤ 2 days (a small transition state-machine domain + one migration for the overlap-exclusion constraint + four lifecycle endpoints + a create-period onboarding seam + boundary reconciliation + authz/idempotency reuse + tests). **Fixtures-first** (the transition matrix is captured before code).

## Re-scope lineage (LL Architect)

WP10 was **carved out of the original P2-WP05 bundle** on 2026-07-11 (recorded in status.md and the WP05 plan). ADR-0005 already noted that "*onboarding and any programmatic first-posting flow must ensure a defined, open period exists first*" and that "*period create/open/close/lock management*" is a follow-up owned by the posting/period application WP — that follow-up is **this WP**. WP05 consumes periods (its open-period guard reads them) but never creates or transitions them; its tests seed periods directly. WP10 supplies the authorized commands that *produce* those period rows and their state transitions.

## Spec sources

- `docs/architecture/rebuild/03-target-architecture.md` §80 (Ledger owns "…periods, close…"; posted entries immutable), §117 ("periods (open/closed with posting-date FK checks)"), §202 (AuthZ pipeline example permission **`period.close`**), §6 (API surface + ProblemDetails).
- `docs/architecture/rebuild/06-feature-roadmap.md` M1 §40 (verbatim): "*Periods: Accounting periods, **open/close/lock enforcement at posting***" — with "*Full period-close engine (equity rollover) in M2*" — so WP10 is the **lifecycle + enforcement**, explicitly **not** the equity close engine.
- **ADR-0005** (posting requires a defined `open` period; onboarding must create an open period before the first posting; `no-period-defined`/`closed`/`locked` reject; overlap prevention was deferred out of WP04 → decided here as **B2**).
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 2 ("posting/reversal/**period** endpoints" is a named Phase-2 deliverable; the exit criterion's "closed-period posts rejected 422" needs the closed periods this WP creates), §5 (salvage vs rewrite; ported behavior pinned by fixtures).
- ADR-0001 (server + DB source of truth), ADR-0002 (uuid ids).

## Goal

Provide the **authorized period-lifecycle commands** that create accounting periods and move them through their legal state transitions, so the WP05 posting guard has real `open` / `closed` / `locked` periods to enforce against, and so onboarding can bootstrap the first open period (ADR-0005):

- `POST …/periods` — **create** a period (name + `[start, end_exclusive)` range), always in `Open` state, rejecting overlaps and invalid ranges.
- `POST …/periods/{id}/close` — **close** an `Open` period (`Open → Closed`).
- `POST …/periods/{id}/reopen` — **reopen** a `Closed` period (`Closed → Open`), never a `Locked` one.
- `POST …/periods/{id}/lock` — **lock** a period (`Open|Closed → Locked`), an **irreversible** privileged action.
- `GET …/periods` — list the space's periods (state + range), for the guard/UI and for onboarding idempotency.
- **Onboarding bootstrap** — a create-first-open-period seam (ADR-0005) that space creation/onboarding calls so a new space always has a defined open period before its first posting.

All commands are `period.manage`-gated (Owner/Admin), RLS-bound txn-locally, idempotent (WP09 seam), and return structured ProblemDetails. The transition legality is ported from the OLD state machine and pinned by golden fixtures. The explicit privileged lock endpoint is a documented target-surface expansion over the OLD system-only lock path; it does not change the OLD transition verdicts.

## Scope

1. **Period-transition domain (pure; fixtures-pinned).**
   - A `PeriodLifecycle` state-machine validator in `LeafLedger.Modules.Ledger.Domain.Periods` that answers "is transition `X → Y` legal?" per the OLD oracle (see Source material): `Open → Closed` allowed, `Closed → Open` allowed, `Open|Closed → Locked` allowed (privileged), `Locked → *` **rejected** (terminal), and self-transitions/no-ops defined explicitly. Consumes the **new transition golden fixtures** (first task). No EF/ASP.NET dependency (Domain purity).
   - A `PeriodRange` guard: `start_date < end_exclusive`; reject inverted/empty ranges with a distinct code.
2. **Overlap-prevention migration (new EF migration `PeriodOverlapConstraint`).**
   - Add a GiST **exclusion constraint** to `periods` mirroring the existing account-code-range pattern (`btree_gist` already enabled): `EXCLUDE USING gist (space_id WITH =, daterange(start_date, end_exclusive, '[)') WITH &&)` so two periods in one space can never cover the same day (B2.1). Raw `migrationBuilder.Sql`; `Down` drops the constraint. No table/column change (see D-WP10-COLUMNS); `HasPendingModelChanges()` must stay green (the constraint is not EF-modelled — verify, mirroring the WP02 code-range exclusion which is also raw SQL).
3. **Lifecycle application + endpoints (Application + Infrastructure + Host).**
   - A `PeriodLifecycleService` (Infrastructure) running each command inside the WP05/WP06 **txn-local binding**: load the period (RLS-scoped), validate the transition via the domain state machine, persist the new state, commit. Create validates range + relies on the DB exclusion constraint as the overlap second wall (unique-violation → structured `period.overlap`).
   - Endpoints in `LedgerEndpoints` (or a sibling `PeriodEndpoints`): `POST …/periods`, `POST …/periods/{id}/close|reopen|lock`, `GET …/periods`. Each write is `period.manage`-gated and **idempotent** via the WP09 `IdempotencyStore` seam.
   - Structured ProblemDetails codes: `period.overlap` (409), `period.invalid_range` (422), `period.invalid_transition` (422, e.g. `Closed → Closed`, `Locked → Open`), `period.locked` (422, mutating a locked period), `period.not_found` (404).
4. **Onboarding open-period bootstrap (ADR-0005).**
   - A `BootstrapOpenPeriodAsync(space, range)` application helper that creates the first open period, safe to call once at space creation. Since the space-creation wizard is Phase 3/M1, WP10 ships the **mechanism** (the helper + the create endpoint) and **documents the precondition** ("space creation must call this before the first posting"); if a space-creation seam already exists in the Host it is wired, otherwise the precondition is recorded as a carry-forward for the onboarding WP. The default bootstrap range/granularity comes from **B2.5**.
5. **Boundary reconciliation (D-WP10-BOUNDARY) — unblocks WP08 I7.**
   - Resolve the flagged off-by-one: the OLD model used an **inclusive** `endDate` (`getPeriodForDate`: `start ≤ date ≤ end`), while the new schema column is `end_exclusive` and P1-WP03 `Period` is half-open `[start, end_exclusive)`. WP10 fixes `PeriodStateResolver.FindPeriod` (currently `date <= EndDate`, inclusive) and the WP05 schema→`PeriodSnapshot` mapping so the canonical rule is **half-open** `start ≤ date < end_exclusive`, with the OLD inclusive fixtures preserved by mapping OLD inclusive `endDate` → new `end_exclusive = endDate + 1 day`. Pinned by a unit test + WP08 I7. (If this changes any WP04/WP01 fixture expectation, it is a documented representation change, not a rule change — the *set of accepted dates* is identical.)
6. **Authorization + idempotency + contract.**
   - Add `ModulePermissions.ManagePeriods = "period.manage"` (Owner/Admin) — subsuming the already-declared `period.close` example (D-WP10-PERM); Member/Viewer denied. Reuse the WP09 idempotency seam on the four writes. Regenerate `leafledger-v1.json` + the TS client (five new operations + 401/403/409/422); the CI `contract` gate stays green.

## Non-goals (explicitly deferred)

- **No equity period-close engine — M2.** No closing journal entry, no income/expense zero-out, no retained-earnings / current-year-earnings rollover, no `executePeriodClose`/`buildClosingJournalEntry`/`buildOpeningBalanceSnapshot` port. WP10 `lock`/`close` are **state transitions only**; the OLD `periodCloseEngine.ts` (equity rollover) and `PeriodClose`/`openingBalancesJson` model are **M2** (roadmap §54). Recorded so QA does not expect an equity close.
- **No FX revaluation** (`fxRevaluationEngine.ts`) — M2.
- **No VAT periods.** The OLD `VatPeriod` entity + `generateVatPeriods` + filed-state machine are a separate subledger — **Phase 4** (VAT). WP10 touches only accounting `periods`.
- **No fiscal-year auto-generation.** The OLD `generateAccountingPeriods` (12-monthly / 4-quarterly tiling from a fiscal-year-start-month + granularity) is a convenience generator. WP10's create endpoint creates **one range at a time**; bulk fiscal-year generation is deferred. The B2.5 bootstrap creates the canonical full-year range for the current fiscal year, so monthly/quarterly subdivision of that year requires a future setup/generation workflow and must not overlap the bootstrap period.
- **No frontend admin-period page.** The OLD `AdminPeriodPage.tsx` is Phase 3/M1 UI; WP10 changes only regenerated `app/src/api/**`.
- **No space-creation wizard.** WP10 provides the bootstrap mechanism + documents the precondition; the onboarding wizard is Phase 3/M1.
- **No `closed_at`/`closed_by` columns unless the API/product explicitly requires them** (see D-WP10-COLUMNS) — the WP02 `audit_log` trigger already records who/when for period mutations.
- **No period deletion** in M1 (the OLD UI allowed deleting empty periods; deletion of a period with postings is unsafe). Deferred; a `DELETE` on an empty period is a plan amendment, not ad-hoc scope.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

### Salvage (behavior oracle → golden fixtures)
- **`src/features/admin/view-model/adminPeriodDataApi.ts` `updatePeriodState`** — the transition state machine (verbatim from its doc + guard): "*`open → closed` allowed; `closed → open` allowed; `* → locked` not allowed via this method (system-only via `lockPeriodsForYear`)*", plus `if (period.state === 'locked') return false;` (**locked is terminal**). This is the **porting oracle** for the WP10 domain state machine and **must be pinned by golden fixtures** (first task → LL Fixture Smith).
- **`src/shared/periodUtils.ts` `getPeriodForDate`** — the date→period lookup with an **inclusive** upper bound (`start ≤ ts ≤ end`); the OLD tests build `endDate = …, 23:59:59.999`. This pins the boundary semantics WP10 must reproduce (via the half-open representation, D-WP10-BOUNDARY). The WP01 period-state fixtures already pin `assertPostingPeriodOpen`; the new fixtures add the **transition matrix** + explicit **inclusive-boundary** cases.
- Note the OLD divergence WP10 *strengthens*: OLD `lock` was **system-only** (via the year-end close, `lockPeriodsForYear`); WP10 exposes an **explicit authorized** `lock` command (the tracker mandates "lock endpoints"). B2.4 authorizes this privileged M1 endpoint; the fixtures still pin OLD transition legality and the divergence is target-surface behavior, not a changed transition verdict.

### Rewrite (spec-derived; no OLD oracle)
- The C# endpoints, the DB **overlap exclusion constraint** (OLD had no hard overlap constraint — periods were non-overlapping only by generator construction; the rebuild makes it a database invariant, §113 "let the database enforce the ledger"), the RLS binding, the authorization gate, and the idempotency wrapping are greenfield per §6/§80/§202. They are pinned by integration + authz + the WP08 I3/I7 properties.

## Accounting decisions

- **A pre-existing pin:** the *posting-time* rejection of closed/locked/undefined periods is already ADR-0005 + WP01 fixtures (consumed WP04/WP05). WP10 does not redecide it.
- **B2 decisions are resolved below.** The OLD transition and boundary fixtures remain byte-unchanged. The only deliberate target divergence is exposing privileged user-invokable `lock` in M1; it does not alter the OLD legality matrix and must be documented in implementation tests/notes.

## B2 decisions (resolved 2026-07-12)

- **B2.1 Overlap — prohibited per space.** Two periods in one space must not cover the same date. Validate at the application boundary for a stable `period.overlap` response and enforce the PostgreSQL GiST exclusion constraint as the authoritative second wall. Overlap across different spaces remains valid.
- **B2.2 Gaps — permitted.** A date outside every period resolves to `no-period-defined` and posting is rejected as `422 posting_period.not_defined` under ADR-0005. The application may report gaps, but must not silently create periods or infer coverage.
- **B2.3 Fiscal structure — arbitrary ranges in M1.** Administrators may create any non-empty `[start_date, end_exclusive)` range with `start_date < end_exclusive`; monthly/quarterly tiling is not required. Fiscal-year bulk generation is deferred as a convenience feature.
- **B2.4 Transition legality — OLD matrix retained, privileged lock exposed.** `Open → Closed`, `Closed → Open`, and `Open|Closed → Locked` are allowed; `Locked → *` is rejected and locked periods are terminal. Same-state lifecycle commands are invalid no-ops, except an exact idempotency replay. Reopening is a privileged, audited administrative correction and never mutates posted entries; corrections remain reversals. The explicit M1 `lock` endpoint is authorized for Owner/Admin and is the deliberate target-surface expansion over OLD's system-only year-end lock path. No unlock endpoint is in scope.
- **B2.5 Onboarding bootstrap — one current fiscal-year period.** Bootstrap creates exactly one `Open` period covering `[fiscal_year_start, next_fiscal_year_start)`. The fiscal-year start month is space configuration; January (`01-01`) is the default when unset. Bootstrap is transactional and idempotent with space initialization and must complete before the first posting. It must not be created implicitly by posting.

These decisions are consistent with OR Art. 957a(2), OR Art. 958f, and GeBüV Art. 3: they provide complete, traceable, auditable, and immutable bookkeeping controls without treating Swiss law as prescribing a particular period-table topology. No new accounting ADR is required; the privileged lock surface is recorded as a WP10 target decision.

## Golden fixtures

**Captured 2026-07-12 by LL Fixture Smith.** WP10 introduces ported accounting-control behavior, so the fixtures-first rule was satisfied by executing the OLD implementation:
- **Transition matrix fixtures** from `updatePeriodState` (+ the `locked` terminal guard): all nine `(from ∈ {Open,Closed,Locked}) → (to ∈ {Open,Closed,Locked})` cells under `tests/fixtures/golden/ledger-core/period-lifecycle/period-transitions/`, with WP01-compatible manifest and `SOURCE.md` provenance.
- **Inclusive-boundary fixtures** from `getPeriodForDate`: date on `startDate`, on the last inclusive day, and one day past under `period-boundaries/`.
The existing WP01 period-state fixtures (posting guard) are reused unchanged. The OLD fixtures stay unchanged; the privileged user-invokable lock is covered as a target-surface decision test and does not change the OLD transition verdicts.

## Decisions

- **D-WP10-PERM — one `period.manage` permission (Owner/Admin).** All four lifecycle writes require `period.manage`; it subsumes the spec's `period.close` example (§202). Member/Viewer denied. (Rationale: create/close/reopen/lock are one privileged administrative capability in M1; a finer split is a later refinement.) Keep the already-declared `ClosePeriod = "period.close"` constant or fold it — implementer's choice, recorded in notes.
- **D-WP10-BOUNDARY — canonical half-open `[start, end_exclusive)`.** Resolve the off-by-one by making the resolver + WP05 mapping half-open (§Scope 5); the accepted-date set is identical to OLD inclusive semantics. Unblocks WP08 I7.
- **D-WP10-COLUMNS — no new period columns by default.** Rely on the WP02 `audit_log` trigger for who/when; add `closed_at`/`closed_by` only if B2.4/product explicitly needs them surfaced in the API (then via this WP's migration). Keeps the migration to the exclusion constraint only and `HasPendingModelChanges()` trivially green.
- **D-WP10-IDEM — reuse the WP09 seam.** The four period writes go through the same `IdempotencyStore` + required `Idempotency-Key` header as the WP05 writes; no second mechanism.

## Dependencies

- **No new production NuGet.** `btree_gist` already enabled; exclusion constraint is raw SQL; endpoints/authz/idempotency/RLS reuse the existing stack. Test project already has the `postgres:17` fixture + TestServer.
- The P1-WP04 OpenAPI pipeline regenerates the contract (five period operations); the CI `contract` gate must stay green.
- **Blocking:** user approval. B2 is resolved and the OLD transition/boundary fixtures are captured.

## File list (implementation target)

**New — golden fixtures (LL Fixture Smith, captured 2026-07-12)**
- `tests/fixtures/golden/ledger-core/period-lifecycle/` (+ root manifest entry, unit manifest, and `SOURCE.md`) — 12 OLD-oracle transition and inclusive-boundary cases.

**New — `backend/src/LeafLedger.Modules.Ledger/Domain/Periods/`**
- `PeriodLifecycle.cs` — the pure transition state-machine validator (`CanTransition(from,to)` + reason) + `PeriodRange` validity. No EF/ASP.NET dependency.

**New — `backend/src/LeafLedger.Modules.Ledger/Infrastructure/Migrations/`**
- `<timestamp>_PeriodOverlapConstraint.cs` — raw `migrationBuilder.Sql` adding the `EXCLUDE USING gist (space_id WITH =, daterange(start_date, end_exclusive, '[)') WITH &&)` constraint; `Down` drops it. No table/column change.

**New — `backend/src/LeafLedger.Modules.Ledger/Application/Periods/` + `Infrastructure/`**
- `IPeriodLifecycleService` + DTOs (create/close/reopen/lock/list) and `BootstrapOpenPeriodAsync` (Application contract).
- `PeriodLifecycleService.cs` (Infrastructure) — txn-local bound command execution, transition validation via the domain, overlap second-wall handling, per-space RLS.
- `PeriodEndpoints.cs` (or extend `LedgerEndpoints.cs`) — the five routes + `period.manage` filter + idempotency + `.Produces` for 200/201/401/403/409/422.

**Modified**
- `backend/src/LeafLedger.Modules.Ledger/Domain/Periods/PeriodStateResolver.cs` — half-open boundary (D-WP10-BOUNDARY).
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/JournalPostingService.cs` — the `ToPeriodSnapshot` mapping aligned to half-open (D-WP10-BOUNDARY).
- `backend/src/LeafLedger.Host/Authorization/ModulePermissions.cs` — add `ManagePeriods = "period.manage"` (Owner/Admin); wire into `Allows`/`IsKnownPermission`.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerModule.cs` + `LeafLedger.Host/Program.cs` — register `IPeriodLifecycleService`; map the five routes with the `period.manage` filter + idempotency.
- `backend/openapi/leafledger-v1.json` + `app/src/api/schema.d.ts` — regenerated (five period operations); **do not hand-edit** the TS.
- `docs/rebuild/plans/P2-WP10-period-lifecycle.md` + `docs/rebuild/status.md` — notes/state.

**New — tests**
- `backend/tests/LeafLedger.Modules.Ledger.Tests/PeriodTransitionGoldenFixtureTests.cs` (unit) — the transition matrix consumes the new fixtures 1:1; the privileged lock surface is covered by a separate target-decision assertion; boundary-inclusivity unit test (D-WP10-BOUNDARY).
- `backend/tests/LeafLedger.IntegrationTests/Ledger/PeriodLifecycleTests.cs` (`[Trait("Category","Integration")]`) — HTTP + `postgres:17`: create (201) / close / reopen / lock happy paths; `Closed → Closed` & `Locked → *` → 422 `period.invalid_transition`; mutating a locked period → 422 `period.locked`; overlapping create → 409 `period.overlap` (DB exclusion second wall); inverted range → 422 `period.invalid_range`; a posting on the last inclusive day of an open period is accepted and one on `end_exclusive` is rejected (I7 preview); a closed/locked period makes a WP05 post return 422 `posting_period.not_open` (I3 preview); idempotent create (same key → replay); no-model-drift stays green.
- `backend/tests/LeafLedger.IntegrationTests/Ledger/PeriodAuthorizationTests.cs` (`[Trait("Category","Integration")]`) — Owner/Admin allowed; Member/Viewer → 403 `auth.permission_denied`; unauthenticated → 401; cross-space period mutation blocked at the filter **and** RLS.

No `*.Domain` dependency on EF/ASP.NET; no equity-close engine; no VAT; no frontend beyond regenerated `api/**`.

## Boundary note

- The transition state machine lives in **Domain** (SharedKernel-only); the service/migration/endpoints in **Infrastructure**/Host. The arch tests (`DomainNamespacesDependOnlyOnSharedKernel`, `EfCoreIsConfinedToInfrastructureNamespaces`) stay green.
- Periods are owned by the Ledger module (D1 single-context baseline); a future extraction is a carry-forward, not this WP.

## Implementation sequence

1. **Obtain user approval** of the resolved B2 decisions recorded above.
2. **Completed 2026-07-12:** capture the transition + boundary golden fixtures from the OLD `updatePeriodState`/`getPeriodForDate`; recapture passed 12/12 and was byte-identical.
3. Implement `PeriodLifecycle` domain + its fixture tests (red → green).
4. Add the `PeriodOverlapConstraint` migration; verify `HasPendingModelChanges()` stays green and the constraint blocks overlaps.
5. Implement `PeriodLifecycleService` + endpoints (create/close/reopen/lock/list) + `period.manage` + idempotency; add the bootstrap helper + document the onboarding precondition.
6. Apply D-WP10-BOUNDARY (resolver + WP05 mapping) + its unit test.
7. Add integration + authorization tests; regenerate the contract.
8. Run Release build + arch + full suite (incl. integration under Docker) + lint/typecheck/page-budget + contract gate; document results/deviations; move to `verify`.

## Acceptance criteria (concrete tests)

1. **Build, boundaries & contract:** `dotnet build LeafLedger.sln -c Release` = 0/0; architecture tests green; `HasPendingModelChanges()` **false** after the constraint migration; the CI `contract` gate green after regenerating the OpenAPI + TS client (five period operations + 401/403/409/422, no unintended drift).
2. **Transition state machine matches the oracle:** the new golden fixtures are consumed 1:1 — `Open→Closed`, `Closed→Open` allowed; `Locked→*` and illegal self-transitions rejected. The privileged user-invokable lock is tested as a target-surface decision while the OLD fixture remains unchanged; no transition verdict is reinterpreted.
3. **Create + overlap second wall:** `POST …/periods` creates an `Open` period (201); an overlapping create is rejected **409** `period.overlap` **at the DB exclusion constraint** (proven by a direct-SQL overlap attempt too, mirroring the account-code-range exclusion); an inverted/empty range → **422** `period.invalid_range`.
4. **Close / reopen / lock:** `close` moves `Open→Closed`; `reopen` moves `Closed→Open`; `lock` moves `Open|Closed→Locked` and is **irreversible** (a subsequent `reopen`/`close`/`lock` on a locked period → **422** `period.locked`/`period.invalid_transition`); illegal transitions → **422** `period.invalid_transition`.
5. **Posting enforcement (I3 preview):** after `close`/`lock`, a WP05 `POST …/journal-entries` dated in that period is rejected **422** `posting_period.not_open`; a post in an undefined-date gap → **422** `posting_period.not_defined` (ADR-0005); a post in an `Open` period succeeds.
6. **Boundary inclusivity (I7 preview, D-WP10-BOUNDARY):** a post dated on the **last inclusive day** of an open period is **accepted**; a post dated on `end_exclusive` is **rejected** (falls outside / into the next period); the resolver + mapping are half-open and the accepted-date set equals the OLD inclusive semantics (unit + integration).
7. **Onboarding bootstrap (ADR-0005):** `BootstrapOpenPeriodAsync` creates the first open period for a new space (per B2.5), is safe to call once, and after it a first WP05 posting in-range succeeds; the onboarding precondition is documented/wired.
8. **Authorization:** Owner/Admin may run all four writes; Member/Viewer → **403** `auth.permission_denied`; unauthenticated → **401**; `period.manage` maps to Owner/Admin only (unit assertion) and unknown/blank role denies.
9. **RLS second wall:** a principal of space A cannot create/close/reopen/lock or list space B's periods — 403 at the filter **and** no cross-space rows via RLS; a no-context connection is fail-closed (mirrors WP02 `OpenAppNoContextAsync`).
10. **Idempotency (WP09 reuse):** each period write requires an `Idempotency-Key`; a retried create with the same key + payload replays (one period, `Idempotent-Replayed: true`); same key + different payload → 409 collision.
11. **Scope containment:** `git diff --name-only` limited to the file list; the migration adds **only** the exclusion constraint (no table/column/trigger/policy change — verified by no-drift); **no** equity-close engine, no VAT, no `*.Domain`→EF dependency, no frontend beyond regenerated `api/**`, no new NuGet; existing Ledger suite stays green.
12. **Quality gate + ProblemDetails:** lint, typecheck, page budgets, arch/boundary, unit + period integration/authorization tests pass in Release; integration runs under Docker (`postgres:17`) on the main-branch job; all error responses are `application/problem+json` with the stable codes above and no PII/stack traces.

## Definition of done

All 12 ACs pass; period create/close/reopen/lock are authorized, idempotent, RLS-bound, overlap-proof (DB exclusion second wall), and transition-legal per the unchanged golden oracle, with the privileged lock surface covered by its target-decision assertion; the onboarding open-period bootstrap exists; the half-open boundary is canonical and pins WP08 I7; closed/locked/undefined periods make WP05 posts reject (WP08 I3 becomes implementable); the migration adds only the constraint with no model drift; the contract regenerates green; no equity-close engine / VAT / frontend / new NuGet. Then state → `verify` and route to LL QA Reviewer. On PASS, **WP08's I3 (closed-period) and I7 (boundary) become implementable** and the Phase-2 finish (WP09 → WP10 → WP08) can complete.

## Sequencing

Recommended Phase-2 finish order: **WP09 (done) → WP10 → WP08**. WP10 is independent of WP08 and depends only on merged WP02–WP06 + WP09. Its first task (fixtures) and user approval must clear before code.

## Risks / notes

- **B2 is resolved.** The policy decisions are recorded above; implementation remains blocked only until the user approves the plan.
- **Fixtures-first is mandatory.** The transition matrix is ported OLD behavior; capture it before writing the state machine so the port is pinned to the oracle (the ADR-0005/WP04 discipline).
- **Lock irreversibility vs Swiss immutability.** `Locked` is terminal (OLD guarantees it; GeBüV immutability supports it). Never allow `Locked → *`. A future "unlock" would be a governed, audited exception — explicitly out of scope.
- **Boundary change is representation-only.** D-WP10-BOUNDARY changes how the upper bound is stored/compared, not which dates are in a period; verify no WP01/WP04 fixture *verdict* flips (only the internal representation). If a verdict would flip, stop and route it as a new accounting decision before implementation.
- **Overlap constraint is the second wall.** The app validates ranges, but the GiST exclusion is the DB invariant (§113). AC3 asserts both; do not rely on app checks alone.
- **No equity close here.** `close`/`lock` are state flags; they do **not** post a closing entry or roll equity. Keep the M2 engine out — bundling it would blow the ≤2-day rule and the M1/M2 boundary.
- **Onboarding seam is partial by necessity.** Space creation is Phase 3; WP10 ships the mechanism + documented precondition. Ensure the create endpoint alone is enough for WP08 to seed closed/locked periods without the wizard.

## Implementation notes

- **2026-07-12 — LL Accounting Expert:** Resolved B2.1–B2.5. Per-space overlaps are prohibited with an application check plus GiST exclusion second wall; gaps are permitted and undefined-date postings reject; M1 accepts arbitrary non-empty half-open ranges and defers bulk fiscal generation; the OLD transition matrix is retained with privileged Owner/Admin lock exposed as a documented target-surface expansion, while `Locked` remains terminal; onboarding bootstraps one transactional/idempotent current-fiscal-year open period using configured fiscal-year start month, defaulting to January. Full-year bootstrap ranges are canonical for that year and cannot later be subdivided without a future non-overlapping setup flow.
- **2026-07-12 — LL Backend Dev:** Implemented the pure transition/range domain, half-open resolver/mapping, GiST overlap-exclusion migration, transactional RLS-bound period service, create/close/reopen/lock/list endpoints, bootstrap helper, `period.manage` authorization, WP09 idempotency replay/collision handling, OpenAPI/TS regeneration, and golden/integration coverage. Updated the shared ledger fixture inventory from 37 to 49 after adding the 12 lifecycle fixtures. Corrected transition responses and idempotency records to use `200 OK`; create remains `201 Created`.
- **2026-07-12 — LL Backend Dev QA remediation:** Added HTTP acceptance coverage for a lifecycle-created period accepting its last inclusive day and rejecting `endExclusive`, rejecting undefined-date gaps, rejecting posts after close or lock, and allowing an in-range post after bootstrap. The lifecycle integration suite is **9/9**; the full Release backend solution is **276/276**. Also corrected EF-owned PostgreSQL connection disposal in the period service and retained structured `period.overlap` mapping for EF-wrapped exclusion violations. Authorization/cross-space coverage is **6/6**; state remains `verify` pending QA sign-off.
- **2026-07-12 — LL Backend Dev validation:** Release backend solution **264/264**; focused period integration **3/3**; frontend lint, typecheck, Vitest **3/3**, strict page budget, production build, and API generation all pass. Remaining QA review gaps are explicit authorization matrix/cross-space cases, bootstrap end-to-end coverage, and posting last-inclusive-day/end-exclusive boundary coverage.
- **2026-07-12 — LL Backend Dev QA remediation:** Added period-route authorization integration coverage for unauthenticated, Owner/Admin, Member/Viewer, and cross-space list/create requests (**6/6**). Bootstrap now returns the existing exact fiscal-year period when invoked again, while conflicting ranges still return `period.overlap`; added a regression for the canonical `[start, end_exclusive)` range. EF-wrapped PostgreSQL exclusion violations map to structured `period.overlap` 409 responses. Full Release backend validation is **271/271**. Explicit posting last-inclusive-day/end-exclusive, gap, and closed/locked rejection coverage remains open for QA; state remains `verify`.
- **2026-07-12 — LL QA Reviewer:** Performed QA review of WP10. Validated test results (276 total tests, 0 failed, 9 period integration tests). Verified build in Release mode and frontend type-checking + linting. Confirmed missing edge cases are adequately covered. The transition fixtures match OLD repo outputs. WP10 is cleanly passing and completes Phase-2 posting readiness. State set to `done`.
- **2026-07-12 — LL Git:** PR #19 merged to `main`. WP10 is complete on the default branch; WP08 is now unblocked for implementation.
