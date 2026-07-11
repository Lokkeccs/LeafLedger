# P2-WP05 — Posting & reversal application + endpoints (eager balance N3, currency/base composition, write-path tenancy binding)

- **Phase:** 2 (ledger core)
- **State:** done — **QA PASS 2026-07-11**. Implementation started 2026-07-11; **B1 and B3 resolved 2026-07-11** by LL Accounting Expert. B2 remains routed to P2-WP10 because it does not affect this WP. Real HTTP/TestServer endpoint and ProblemDetails coverage passed independent QA reproduction.
- **Owner (implementation):** LL Backend Dev
- **Depends on:** P2-WP02 (schema: `journal_entries`/`journal_lines`/`line_attributions`/`periods`/`accounts`, RLS second wall, deferred balance trigger, `leafledger_app` role, audit triggers), P2-WP03 (ChartOfAccounts `CurrencyPolicyEvaluator` — composed, not re-implemented), P2-WP04 (Ledger `JournalEntry` aggregate + `Reverse(...)`, `PostingValidityEvaluator`, `PeriodStateResolver` — driven, not modified), P1-WP04 (OpenAPI → generated-client pipeline: the two new endpoints regenerate the contract).
- **Blocks:** P2-WP06 (AuthZ hardens the tenancy binding this WP introduces), P2-WP07 (reporting reads posted entries), P2-WP08 (property suite drives these endpoints), **P2-WP09** (idempotency middleware wraps these endpoints), **P2-WP10** (period-lifecycle commands produce the periods this WP's guard reads).
- **Estimated size:** ≤ 2 days (application/posting service + two write endpoints + persistence mapping + write-path tenancy binding + entry_no allocation + 422 ProblemDetails + integration tests + contract regen). **No idempotency middleware and no period write commands** — see the re-scope note.

## Re-scope note (LL Architect, 2026-07-11)

The original tracker row bundled *posting + reversal + period open/close/lock + idempotency (N2) + eager balance (N3)* into one WP. That exceeds the ≤ 2-day / independently-verifiable rule, so Phase 2 is decomposed as follows (status.md updated in the same change):

- **P2-WP05 (this plan)** — posting + reversal application service and the two write endpoints; eager balance 422 (N3 app half); currency-policy + base-minor-unit composition (WP03 A1 / B1); **write-path** tenancy binding (resolves carry-forward `N-WP06-pool` for the posting transaction); per-space `entry_no` allocation (B3).
- **P2-WP09 (new, proposed)** — idempotency middleware (N2): `idempotency_keys` table migration, client-ULID unique index, 24 h TTL + cleanup, replay, collision metric. Depends on WP05.
- **P2-WP10 (new, proposed)** — period-lifecycle commands (create / open / close / lock; overlap policy B2) + onboarding open-period bootstrap (ADR-0005). Depends on WP05.

WP05 **consumes** periods (the open-period guard reads them) but does **not** create or transition them; its tests seed periods directly (as the WP02 integration fixtures already do). WP08's exit gate depends on WP05 + WP09 + WP10 together (exactly-once + closed-period rejection).

- **Spec sources:** `docs/architecture/rebuild/03-target-architecture.md` §6 (API surface — `POST /api/v1/spaces/{spaceId}/journal-entries` with `Idempotency-Key` header + 422 structured; `.../journal-entries/{id}/reverse`; **ProblemDetails everywhere**; `/api/v1` prefix), §3–§4 (Ledger is the sole writer; posted entries immutable; money = integer minor units; deferred balance trigger `SUM(base_amount_minor)=0` as the DB second wall), §7 (Result-based domain errors → ProblemDetails, global exception handler); `docs/architecture/rebuild/04-implementation-plan.md` §Phase 2 (posting rules already ported; this WP is the application/endpoint layer) + §5 (port-then-refactor, smallest viable diff); `docs/rebuild/plans/NOTES-risk-review-2026-07-06.md` **N2** (idempotency lifecycle — now WP09) and **N3** (eager balance check — app half here); ADR-0005 (posting requires a defined `open` period); ADR-0002 (uuid id storage); ADR-0001 (online-first, server/DB are the source of truth).

## Goal

Turn the pure Ledger domain (WP04) into a working **write path**: an application/posting service that loads the reference snapshots a space needs, composes the already-built evaluators (posting-validity for accounts + currency policy + open-period guard) with the `JournalEntry` aggregate, allocates a per-space `entry_no`, persists the aggregate to the WP02 tables inside one RLS-bound transaction, and exposes it as `POST /journal-entries` and `POST /journal-entries/{id}/reverse` with structured **422 ProblemDetails** on any invariant violation. This is the first real business write surface in the Host.

The eager balance check (N3 app half) lives here: the request is rejected **422 before COMMIT** by constructing the domain aggregate (which cannot be built imbalanced), while the WP02 deferred trigger remains the independent DB second wall. Idempotency (N2) and period-lifecycle writes are explicitly out of scope (WP09 / WP10).

## Scope

1. **Application layer in `LeafLedger.Modules.Ledger`** (new `Application/` namespace; may reference EF via `Infrastructure`, never leak EF types into `Domain`):
   - `PostJournalEntryCommand` / `ReverseJournalEntryCommand` request models (plain DTOs, minor-unit `long` amounts, ISO `string` currencies, `DateOnly` dates, nullable opaque `vat_code_id`/`business_partner_id`/`project_id`, optional per-line attributions).
   - A posting service that, for a given `spaceId` + actor:
     1. loads reference snapshots from the DB (accounts with `is_active`/`valid_from`/`valid_to`/`kind`/`currency`; periods with `state`/`start_date`/`end_exclusive`) — mapped into the plain snapshot shapes the WP04/WP03 evaluators already accept;
   2. runs the guards in a defined order (see §Ordering) — **currency present + valid ISO** → currency policy (WP03) → account posting-validity (WP04) → open-period guard (WP04/ADR-0005) → base-minor-unit validation (B1) → aggregate construction (exact-integer balance, attribution 1000‰);
   3. allocates a monotonic, unique per-space `entry_no` (B3) and persists the aggregate → WP02 POCOs in one transaction;
   - returns a `Result`-shaped outcome carrying either the created entry id/`entry_no` or a structured error list.
2. **Two write endpoints in the Host** (registered via a module endpoint-extension so the Host stays EF-free where practical):
   - `POST /api/v1/spaces/{spaceId}/journal-entries` → 201 with the created entry; **422** ProblemDetails on any invariant/validity/period/currency/balance violation; 400 on malformed request.
   - `POST /api/v1/spaces/{spaceId}/journal-entries/{id}/reverse` → 201 with the reversal entry (body carries the explicit reversal `date` per A2); 422 if the reversal date does not resolve to an `open` period, or if the target is missing / already reversed per the chosen policy; 404 if the target entry does not exist in the space.
   - Both accept an `Idempotency-Key` header **field** but its enforcement is WP09; this WP documents the header and MAY parse/log it, but MUST NOT implement replay/storage (state that explicitly so WP09 owns it and QA does not expect it here).
3. **Eager balance check (N3 app half):** an unbalanced request (`SUM(base_amount_minor) ≠ 0`) is rejected **422 `journal_entry.unbalanced` before COMMIT** by the domain factory; the WP02 deferred DB trigger stays as the second wall and MUST also reject a crafted direct-SQL unbalanced insert (proven by an integration test that bypasses the app path).
4. **Currency + base-minor-unit composition (WP03 A1 / resolved B1):** reject absent/invalid transaction currency on any line; run `CurrencyPolicyEvaluator` (balance-sheet/equity lines must match the account's fixed currency; P&L any); require client-supplied `base_amount_minor` and validate it in the space base currency; the base amounts must net to zero. Same-currency lines require `base_amount_minor == amount_minor` and no `fx_rate` or `fx_rate == 1`; foreign-currency lines require a positive `fx_rate`, matching sign, and deterministic `AwayFromZero` conversion consistency within one base minor unit.
5. **Write-path tenancy binding (resolves `N-WP06-pool`):** the posting/reversal transaction runs as `leafledger_app` with `app.current_space_id` + `app.current_actor` bound **transaction-locally** (`SET LOCAL ROLE` + `set_config(..., is_local => true)`), so pooled connections cannot leak a prior space's GUC (the WP02 fixtures used `is_local => false`; this WP MUST use `true`). `spaceId` comes from the route and is **trusted input pending WP06** (no authenticated principal yet); this WP exercises RLS as the tenancy second wall but does **not** implement authorization.
6. **Per-space `entry_no` allocation (resolved B3):** allocate a monotonic, never-reused `bigint` `entry_no` under the existing unique index `IX_journal_entries_space_id_entry_no`; allocation must be concurrency-safe, per space overall (not per fiscal year), and may leave an explainable gap when a transaction rolls back.
7. **ProblemDetails mapping:** a single mapping from `Result`/domain-error lists → `application/problem+json` with a stable machine-readable `code` per error (`journal_entry.unbalanced`, `posting_validity.*`, `currency_policy.currency_not_allowed`, `currency.invalid`, `posting_period.not_open`, `posting_period.not_defined`, `line_attribution.*`), a 422 status, and a structured `errors[]`/`issues[]` array preserving input order. Codes standardized here per the WP04 carry-forward.
8. **Contract + tests:** regenerate `backend/openapi/leafledger-v1.json` + the generated TS client (the CI `contract` gate must stay green); add integration tests (Testcontainers, `[Trait("Category","Integration")]`) that post through the real HTTP + RLS path and assert both walls.

### Ordering (composition)

Guards evaluated in a fixed, documented order so error precedence is deterministic (referenced by ACs and the WP08 property suite). Proposed: **request well-formedness (400)** → **currency present + valid ISO** → **currency policy** → **account posting-validity** → **open-period guard** → **base-minor-unit balance / aggregate construction**. Whether all failing guards are collected into one 422 `issues[]` or the first failing guard short-circuits is an implementation decision to record; the OLD `JournalEntryPage` save flow (reference only) is not authoritative for ordering.

## Non-goals (explicitly deferred)

- **No idempotency middleware (N2) — WP09.** No `idempotency_keys` table/migration, no replay, no 24 h TTL/cleanup, no collision metric. WP05 only reserves the `Idempotency-Key` header contract.
- **No period write commands — WP10.** No create/open/close/lock endpoints, no onboarding open-period bootstrap. WP05 reads existing periods for the guard; tests seed them directly.
- **No authorization — WP06.** No JWT/principal, no scope→license→role→permission pipeline. The route `spaceId` is trusted; endpoints are unauthenticated. **Security caveat:** these endpoints MUST NOT be exposed to a production/public environment until WP06 (authz) + WP09 (idempotency) land; nothing is deployed in Phase 2, so this is acceptable in-repo. RLS is still bound so cross-tenant isolation holds even without authz.
- **No partner/project/VAT validity composition.** No `business_partners`/`projects`/`vat_codes` catalog tables exist (M2 feature WPs); `vat_code_id`/`business_partner_id`/`project_id` are accepted as **opaque nullable uuids** (no FK target today) and are not validity-checked. The WP04 partner/user/project evaluators are not wired until their catalogs exist.
- **No reporting / list endpoints.** `GET /journal-entries` (cursor pagination), trial-balance/reports, and `/integrity` are WP07.
- **No FX arithmetic / rate lookup / revaluation / VAT calculation.** No market-data source exists (Phase 5); base-amount handling in M1 is validation-only per B1, never a server-side rate lookup.
- **No schema change beyond what already exists.** WP05 persists into the WP02 tables as-is; it adds **no migration** (the idempotency table is WP09). If mapping reveals a genuinely missing column, that is a plan amendment, not an ad-hoc migration.
- **No domain change.** `JournalEntry`, `Reverse(...)`, and the evaluators are driven as-is; any needed domain change is a WP04 amendment, not silent editing.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

### Reference only (not salvaged as code)
- `src/features/journal-entry/JournalEntryPage.tsx` — the OLD client-side save flow: sequence of `assertPostingPeriodOpen` → currency policy → account/partner validity, then a **float** balance check (`Math.abs(delta) < 0.01`). Used **only** as a reference for guard ordering; the float balance is explicitly rejected (exact-integer per WP04). The OLD "edit/delete to correct" flow is replaced by immutable reversal (WP04).
- OLD idempotency: none (the OLD system had no idempotency-key mechanism) — N2 is a spec-derived rewrite, and it is **WP09**, not this WP.

### Rewrite (spec-derived; no OLD oracle)
- The **application/posting service**, endpoint contracts, ProblemDetails mapping, tenancy binding, and `entry_no` allocation are greenfield per Part 3 §6/§7 and ADR-0001/0002/0005. They are pinned by **integration tests + the WP08 property suite**, not by golden fixtures (the *rules* they compose are already fixture-pinned in WP03/WP04).

## Accounting decisions (LL Accounting Expert)

WP05 is not a new rule port, but it makes orchestration choices with Swiss-accounting implications. B1 and B3 are resolved below; B2 is carried to WP10 because WP05 only reads periods.

- **B1 — Base-currency handling at posting time (M1) — RESOLVED 2026-07-11.** Accept multi-currency postings with **client-supplied `base_amount_minor` per line, strictly server-validated**; do not add FX rate lookup or revaluation in WP05. Transaction currency must be present and valid ISO. When line currency equals the space base currency, require `base_amount_minor == amount_minor` and `fx_rate` absent or exactly 1. When currencies differ, require a positive `fx_rate`, matching sign, and `base_amount_minor` consistent with `amount_minor × fx_rate` using deterministic `AwayFromZero` rounding, within one base minor unit. Require `SUM(base_amount_minor) == 0`. Persist the supplied base amount and rate as immutable posting evidence; later FX/revaluation WPs may create correcting entries but must not mutate posted lines. Because this validation introduces posting-time FX translation/rounding behavior, the agreed rounding cases require new golden fixtures before implementation. The server must not silently invent an exchange rate or trust an unexplained base amount.
- **B2 — Period overlap/gap policy (for WP10, surfaced now).** ADR-0005/A1 deferred overlap policy. Must period creation prevent overlapping periods (and require contiguity/no gaps)? This does not block WP05 (which only reads periods) but is recorded so WP10 planning has the answer. *(Route with B1/B3 or at WP10 planning.)*
- **B3 — Journal-entry numbering — RESOLVED 2026-07-11.** Use **monotonic, unique, never-reused numbering per space overall**; gaps are permitted when a transaction rolls back, provided they remain explainable through transaction/audit evidence. Do not partition by fiscal year in M1. Allocation must be concurrency-safe and must not use `MAX(entry_no) + 1`; use a database-backed or serialized per-space allocator compatible with the existing `(space_id, entry_no)` unique index. Gapless numbering is not required for this WP: Swiss requirements emphasize chronological, complete, and verifiable records, but do not require an absolutely gapless sequence in every implementation.

## Golden fixtures

**No new golden fixtures are required for the composition itself.** The rules WP05 composes are already pinned: posting-validity + period-state by the 37 P2-WP01 fixtures (consumed in WP04), currency policy by the 11 currency fixtures (consumed in WP03). The application wiring (guard ordering, ProblemDetails codes, `entry_no`, tenancy binding, both-walls balance) is pinned by **integration tests + the WP08 property suite**, which is the correct instrument for orchestration behavior.

**Fixture Smith result — 2026-07-11:** the OLD checkout is pinned at SHA `085bedba467e3d46d3889db3bc80ea023e69756e`; its `src/shared/fxPolicy.ts` contains only policy/metadata helpers, and the OLD repo has no posting-time `base_amount_minor` conversion or validation function to execute. The checkout had unrelated pre-existing generated-file changes; this task added no files or changes there. Per Fixture Smith rules, the nine canonical B1 cases were therefore added under `tests/fixtures/golden/ledger-posting-base/` with `captureStatus: UNVERIFIED` and explicit per-case `expected.status: UNVERIFIED` markers. They are **not** golden oracles and must not be consumed as exact expected outputs. Coverage includes positive/negative foreign amounts, exact and half-unit rounding, same-currency `base == amount`, invalid/non-positive rates, sign mismatch, one-minor-unit tolerance, and a balanced multi-currency posting. Once the target validator exists, LL Backend Dev must turn these canonical inputs into target-decision assertions; if an executable OLD equivalent is later found, recapture and replace the markers rather than silently promoting them.

## Dependencies

- Test-only NuGet: `Microsoft.AspNetCore.Mvc.Testing` 9.0.0 and `Microsoft.AspNetCore.TestHost` 9.0.0; the integration test project references `LeafLedger.Host` to exercise the real minimal-API pipeline through `WebApplicationFactory<Program>`.
- The OpenAPI build-time doc + `openapi-typescript`/`openapi-fetch` pipeline (P1-WP04) regenerates the contract for the two new endpoints; the CI `contract` diff gate must stay green.

## File list (implementation target)

**New — `backend/src/LeafLedger.Modules.Ledger/Application/`**
- `Posting/PostJournalEntryCommand.cs` (+ line/attribution request records)
- `Posting/ReverseJournalEntryCommand.cs`
- `Posting/JournalPostingService.cs` (loads snapshots, composes guards, allocates `entry_no`, persists in one RLS-bound transaction)
- `Posting/PostingResult.cs` / error model (maps to ProblemDetails)
- `Snapshots/` mappers: WP02 POCOs → the plain reference snapshots the WP03/WP04 evaluators accept (incl. `periods.end_exclusive` → the domain's inclusive `EndDate` = `end_exclusive − 1 day`).

**New — `backend/src/LeafLedger.Modules.Ledger/Infrastructure/` (or `Endpoints/`)**
- Persistence mapping aggregate ⇄ `journal_entries`/`journal_lines`/`line_attributions` POCOs.
- Transaction + tenancy-binding helper (`SET LOCAL ROLE leafledger_app` + txn-local `app.current_space_id`/`app.current_actor`).
- `LedgerEndpoints.cs` module extension registering the two `MapPost` routes + ProblemDetails mapping (called from `Program.cs`).

**Modified**
- `backend/src/LeafLedger.Host/Program.cs` — call the Ledger endpoint-registration extension; ProblemDetails/exception-handler wiring; ensure the runtime connection can `SET ROLE leafledger_app` (config note, no secret).
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerModule.cs` — expose the posting service + endpoint registration via DI.
- `backend/openapi/leafledger-v1.json` — regenerated (two new endpoints).
- `app/src/api/schema.d.ts` (+ `client.ts` if the generator changes it) — regenerated; **do not hand-edit** generated files.
- `backend/LeafLedger.sln` — add any new test project if not already present.
- `backend/tests/LeafLedger.IntegrationTests/Ledger/LedgerHttpEndpointTests.cs` — real TestServer + PostgreSQL HTTP coverage for both endpoints, ProblemDetails, actor validation, and route behavior.
- `backend/tests/LeafLedger.IntegrationTests/LeafLedger.IntegrationTests.csproj` — test-only ASP.NET TestHost packages and Host project reference.
- `backend/src/LeafLedger.Host/Program.cs` — public factory entry-point marker only; no runtime behavior change.
- `docs/rebuild/plans/P2-WP05-posting-reversal-endpoints.md` + `docs/rebuild/status.md` — notes/state.

**New — tests**
- `backend/tests/LeafLedger.Modules.Ledger.Tests/` — application-level unit tests for guard ordering, ProblemDetails codes, snapshot mapping (incl. `end_exclusive` inclusivity), base-amount validation (per B1).
- `backend/tests/LeafLedger.IntegrationTests/Ledger/` — HTTP + RLS integration tests (`[Trait("Category","Integration")]`): happy-path 201; unbalanced → 422 before COMMIT; closed/locked/no-period → 422; currency-policy violation → 422; cross-tenant isolation via RLS; DB trigger rejects a crafted direct-SQL unbalanced insert (second wall); concurrent posts get distinct `entry_no`; reversal into an open period succeeds and links `reverses_entry_id`; reversal into a closed period → 422.

No files under `app/src/**` except regenerated `api/**`; no `backend/src/LeafLedger.Modules.Ledger/Domain/**` change; no new migration.

## Boundary note

- `Domain` stays pure (SharedKernel only). The new `Application`/`Infrastructure`/endpoint code may use EF Core, but **EF types must not appear in `Domain`** — the architecture test `EfCoreIsConfinedToInfrastructure` must stay green (the `Application` posting service orchestrates but persistence/EF lives in `Infrastructure`; if the service needs EF, place it under `Infrastructure` or inject an `Infrastructure` repository).
- Ledger `Application` MAY reference **ChartOfAccounts' public contract** (`CurrencyPolicyEvaluator`) — cross-module public contracts are allowed; `*.Domain` boundaries are unchanged.

## Implementation sequence

1. Review the nine canonical B1 inputs under `tests/fixtures/golden/ledger-posting-base/`; keep them explicitly `UNVERIFIED` because the OLD implementation has no executable equivalent.
2. Add the request DTOs + snapshot mappers (incl. `end_exclusive` inclusivity); unit-test the mapping.
3. Add `JournalPostingService`: compose currency guard → currency policy → account validity → open-period guard → B1 base-amount validation → aggregate; unit-test ordering + ProblemDetails codes with in-memory snapshots and promote the canonical inputs into target-decision assertions without labeling them OLD fixtures.
4. Add persistence mapping + txn-local tenancy binding + monotonic per-space `entry_no` allocation; wire the two endpoints in the Host; regenerate the OpenAPI contract + TS client.
5. Add integration tests through the real HTTP + RLS path: happy path, both balance walls, closed/locked/no-period, currency-policy, B1 conversion validation, cross-tenant isolation, concurrent `entry_no`, rollback gap/non-reuse, reversal open/closed.
6. Run Release build + arch tests + full suite (incl. integration under Docker) + lint/typecheck/page-budget + contract gate; document results/deviations; move to `verify`.

## Acceptance criteria (concrete tests)

1. **Build, boundaries & contract:** `dotnet build LeafLedger.sln -c Release` = 0/0; architecture tests stay green (`*.Domain` → SharedKernel only; EF confined to `Infrastructure`); the CI `contract` gate is green after regenerating `leafledger-v1.json` + the TS client (no drift).
2. **Happy-path posting:** `POST /api/v1/spaces/{spaceId}/journal-entries` with a balanced ≥2-line body returns **201** with the created entry id and an allocated `entry_no`; the rows land in `journal_entries`/`journal_lines` (+ `line_attributions` when supplied) under the `leafledger_app` role with tenancy bound.
3. **Eager balance (N3 app half) — first wall:** an unbalanced body (`SUM(base_amount_minor) ≠ 0`) returns **422 `journal_entry.unbalanced` before any COMMIT** (no partial rows persisted).
4. **DB trigger — second wall:** a crafted **direct-SQL** unbalanced insert (bypassing the app path, as `leafledger_app`) is rejected by the WP02 deferred balance trigger at COMMIT — proving both walls exist independently.
5. **Period guard (ADR-0005):** posting into a `closed`/`locked` period → **422 `posting_period.not_open`**; posting when no period contains the date → **422 `posting_period.not_defined`**; posting into an `open` period succeeds. (Periods seeded directly; WP10 owns their write commands.)
6. **Currency composition (WP03 A1 / resolved B1):** a line with absent/invalid currency → **422 `currency.invalid`**; a balance-sheet/equity line whose currency ≠ the account's fixed currency → **422 `currency_policy.currency_not_allowed`**; a P&L line accepts any valid currency; same-currency lines require `base_amount_minor == amount_minor` and absent/one `fx_rate`; foreign-currency lines require a positive rate, matching sign, and `AwayFromZero` conversion consistency within one base minor unit; the base sum must be 0. The focused golden fixture set covers the rounding and rejection cases.
7. **Reversal:** `POST /journal-entries/{id}/reverse` with an explicit `date` in an **open** period returns **201** with a new entry whose lines negate the original and whose `reverses_entry_id == {id}`; a reversal `date` in a `closed`/`locked`/undefined period → **422**; a non-existent target → **404**; the original entry is unchanged (append-only).
8. **Tenancy second wall (RLS):** a request bound to space A cannot read or write space B's rows; a posting transaction runs with **txn-local** `app.current_space_id`/`app.current_actor` (`is_local => true`) as `leafledger_app`, and a subsequent pooled connection with no context is fail-closed (no GUC leak) — mirroring the WP02 `OpenAppNoContextAsync` guarantee.
9. **`entry_no` allocation (resolved B3):** concurrent posts to the same space produce **distinct, monotonic, never-reused** `entry_no` values honoring the unique index; numbering is per space overall, not per fiscal year; a rolled-back posting may leave a gap, and a later post must not reuse the burned number. The gap/non-reuse behavior is asserted by test.
10. **ProblemDetails shape:** every failure returns `application/problem+json`, 422 for invariant/validity/period/currency/balance violations (400 for malformed request, 404 for missing target), with a stable machine `code` per the standardized set and a structured `errors[]`/`issues[]` in input order; guard-ordering precedence is asserted.
11. **Scope containment:** `git diff --name-only` is limited to the file list; **no new migration**, no `Domain` change, no idempotency storage/replay, no period write commands, no authz, no partner/project/VAT validity; the existing Ledger integration suite (incl. `HasPendingModelChanges()`) stays green (schema untouched).
12. **Accounting traceability:** B1 and B3 are recorded here as resolved 2026-07-11 decisions, the code matches them, and the nine canonical B1 cases are present with explicit `UNVERIFIED` status because no OLD executable exists. They must become target-decision assertions before implementation is complete, but must not be described as OLD golden matches. No accounting behavior is inferred by the developer.
13. **Quality gate:** lint, typecheck, page budgets, arch/boundary tests, and the relevant unit + financial-invariant integration tests all pass in Release; integration tests run under Docker (`postgres:17`) via the `Category=Integration` filter and are green on the main-branch job.

## Definition of done

B1 (and B3) recorded and matched; all 13 ACs pass; both balance walls proven; RLS tenancy isolation and txn-local binding proven; the two endpoints regenerate a green OpenAPI contract; no migration / no `Domain` / no out-of-scope change; Release build clean; full suite (unit + arch + integration) green. Then state → `verify` and route to LL QA Reviewer. Idempotency (WP09) and period lifecycle (WP10) are tracked separately; WP05 endpoints are **not production-ready** until WP06 + WP09 land (recorded as a cross-WP invariant).

## Risks / notes

- **Two walls, not one.** N3 is deliberately redundant: the app rejects imbalance *before* COMMIT (fast, structured 422) and the DB trigger rejects it *at* COMMIT (authoritative). Both must be independently tested; do not remove either.
- **Base-currency validation (B1 resolved).** No FX source exists in Phase 2, so the client supplies the rate and base amount; the server validates deterministic conversion consistency and records both immutably. The new golden fixtures protect the rounding boundary. No server-side rate lookup or revaluation belongs here.
- **`entry_no` concurrency (B3 resolved).** Monotonic-unique per-space numbering permits rollback gaps and avoids the serialization required by gapless numbering. The allocator must still be concurrency-safe, never reuse numbers, and preserve evidence sufficient to explain gaps.
- **Period `end_exclusive` vs domain inclusive `EndDate`.** The schema stores half-open `[start_date, end_exclusive)`; `PeriodStateResolver.FindPeriod` compares against an inclusive `EndDate`. The snapshot mapper MUST convert (`EndDate = end_exclusive − 1 day`) or the guard will be off-by-one at period boundaries — pinned by a boundary test (aligns with the WP04 carry-forward on period inclusivity, which WP08 also pins).
- **Tenancy binding must be txn-local.** The WP02 fixtures used `set_config(..., false)` (session GUC) which leaks across pooled connections (carry-forward `N-WP06-pool`). WP05 MUST use `SET LOCAL` / `is_local => true` so the posting transaction cannot inherit or leak a space context. WP06 generalizes this into the authenticated request pipeline.
- **Unauthenticated in Phase 2.** Route `spaceId` is trusted until WP06. Acceptable only because nothing is deployed; RLS still enforces isolation. Flag prominently so no one exposes these endpoints early.
- **No idempotency yet.** Retried POSTs will double-post until WP09. WP08's exactly-once property therefore depends on WP09; sequence WP09 immediately after WP05.

## Implementation notes

- **2026-07-11 — LL Backend Dev:** Implementation started with focused TDD for the B1 integer-safe base-amount validator. No Domain, migration, authz, idempotency, or period-lifecycle changes are planned.
- **2026-07-11 — LL Backend Dev:** Implemented posting/reversal DTOs and service, B1 exact-rational validation with AwayFromZero rounding, currency/account/period guard composition, EF persistence, txn-local RLS binding, advisory-locked per-space entry numbering, thin Host endpoints, and regenerated OpenAPI/TS schema. Focused PostgreSQL coverage is 5/5 (happy path, eager imbalance/no rows, end-exclusive boundary, reversal/closed period, concurrency); full backend integration is 42/42 and non-integration is 143/143. Frontend tests/lint/typecheck/page budget/build are green. State remains `in-progress` because real HTTP/TestServer endpoint assertions and HTTP ProblemDetails coverage were not added in this session.
- **2026-07-11 — LL Backend Dev:** Added `LedgerHttpEndpointTests` using `WebApplicationFactory<Program>` + TestServer against the shared Testcontainers PostgreSQL fixture. HTTP coverage is 4/4: posting 201 with `Location`/body, unbalanced 422 `application/problem+json` with stable `journal_entry.unbalanced` in both `errors[]` and `issues[]`, reversal 201 with `reversesEntryId`, missing target 404 with `journal_entry.not_found`, missing/malformed `X-Actor-Id` 400, and GET route 405. Added only test packages `Microsoft.AspNetCore.Mvc.Testing`/`Microsoft.AspNetCore.TestHost` 9.0.0, a Host project reference, and a public `Program` factory marker. Release build 0/0; architecture 3/3; full Release integration 46/46; frontend tests 3/3, lint, typecheck, page budget, and build green. Contract regeneration is deterministic but `git diff --exit-code` reports the expected uncommitted WP05 OpenAPI/TS additions already present in the workspace; no generated files were reverted. State → `verify`, pending LL QA Reviewer.

- **2026-07-11 — LL Backend Dev:** Fixed QA findings: reversal now reconstructs the source through `JournalEntry.Reverse(...)`, loads and persists every `LineAttribution`, and preserves persisted FX metadata without checked negation; the application boundary therefore returns the domain `journal_entry.amount_out_of_range` failure for `long.MinValue`. Added real Testcontainers PostgreSQL coverage for attribution preservation and real TestServer HTTP coverage for a balanced extreme source entry returning 422 `application/problem+json` with the stable code in both `errors[]` and `issues[]`. Focused regression tests **2/2** green; existing `PostingServiceTests` **5/5** green.

- **2026-07-11 — LL Fixture Smith:** inspected the OLD checkout at SHA `085bedba467e3d46d3889db3bc80ea023e69756e`; `src/shared/fxPolicy.ts` provides policy/metadata only and no base-amount conversion/validation path exists. The OLD checkout had unrelated pre-existing generated-file changes; this task added no files or changes there. Added nine canonical B1 input cases under `tests/fixtures/golden/ledger-posting-base/`; all are explicitly `UNVERIFIED`, with no hand-authored expected accounting outputs.

## QA verdict

**PASS -> done — LL QA Reviewer, 2026-07-11.** Independently reproduced the Release build, architecture/boundary tests, backend unit and PostgreSQL integration suites, HTTP/TestServer endpoint coverage, frontend tests/lint/typecheck/page budget/build, and deterministic contract generation. Posting, reversal, ProblemDetails, actor validation, route behavior, attribution preservation, extreme-amount handling, RLS/transaction-local tenancy, both balance walls, period guards, currency/base validation, and entry numbering passed. The nine canonical B1 cases remain explicitly `UNVERIFIED` because the OLD repository has no executable base-amount validator; they are target-decision assertions, not OLD golden matches. WP05 is done, with production exposure still gated by WP06 authorization and WP09 idempotency as documented non-goals.
