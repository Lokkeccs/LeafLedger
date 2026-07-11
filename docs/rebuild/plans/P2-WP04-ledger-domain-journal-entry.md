# P2-WP04 — Ledger domain: JournalEntry aggregate, posting-validity rules, reversal linkage, immutability, exact-integer balance

- **Phase:** 2 (ledger core)
- **State:** done — QA PASS 2026-07-11 (LL QA Reviewer, independently reproduced); Release 0/0; full suite 176/176. Cleared for LL Git.
- **Owner (implementation):** LL Backend Dev
- **Depends on:** P2-WP01 (37 posting-validity + period-state golden fixtures — the executable oracle: posting-accounts 10, business-partners 7, users 7, projects 5, period-state 8), P2-WP03 (ChartOfAccounts domain — currency policy is owned there and composed later, not re-implemented here), P1-WP03 (`Money`/minor units, `Id<T>`, `Result`/`DomainError`).
- **Blocks:** P2-WP05 (posting/reversal/period *application* + endpoints composes this domain), P2-WP07 (reporting reads posted entries), P2-WP08 (financial-invariant property suite exercises the aggregate + DB walls).
- **Estimated size:** ≤ 2 days (pure `Domain` namespace added to the existing `LeafLedger.Modules.Ledger` module + fixture consumer + unit tests; no persistence, no endpoints).
- **Spec sources:** `docs/architecture/rebuild/03-target-architecture.md` §3 (Ledger owns "Journal entries, lines, attributions, periods, close … Sole writer of postings; posted entries **immutable** — corrections are reversal + new entry"), §4.1 (money = integer minor units, **no floats near amounts**; immutability via `reverses_entry_id`), §4.2 (schema sketch: signed `amount_minor`, `base_amount_minor`, `line_attributions … sum = 1000 enforced`, deferred balance trigger `SUM(base_amount_minor)=0`), §5 (module/Domain boundaries — `*.Domain` references nothing but SharedKernel); `docs/architecture/rebuild/04-implementation-plan.md` §Phase 2 ("posting rules ported from `postingValidity.ts` semantics, float tolerances → exact integer math") + §5 (salvage vs rewrite, port then refactor); `docs/architecture/rebuild/05-quality-and-maintainability.md` §1.1/§1.3 (golden-master oracle + pure-domain coverage).

## Goal

Add the pure C# **Ledger domain** to the existing `LeafLedger.Modules.Ledger` module (which today holds only `Infrastructure` from P2-WP02): the `JournalEntry` aggregate (entry + lines + attributions), the posting-validity rules ported from the OLD `postingValidity.ts` against the P2-WP01 fixtures, period-state resolution, reversal linkage, aggregate immutability, and the **exact-integer** balance invariant. The deliverable is persistence-free domain code that P2-WP05 will drive from endpoints without the domain referencing EF Core or the anemic WP02 POCOs.

This WP separates the **behavioral port** (posting-validity, pinned by fixtures) and the **spec-derived rewrite** (aggregate structure, exact-integer balance, reversal, immutability, attribution 1000‰) from the later application/endpoint/idempotency work (WP05) and the DB-trigger/property walls (WP02/WP08).

## Scope

1. **`Domain` namespace in `LeafLedger.Modules.Ledger`** — pure, references only SharedKernel; no EF/AspNetCore/Npgsql; no dependency on other modules' domains.
2. **`JournalEntry` aggregate** (entry + lines + attributions):
   - identity (`Id<JournalEntryTag>`), space id, entry date (`DateOnly`), description/reference, `created_by`, optional `ReversesEntryId` (self-link), status;
   - a private constructor + static factory returning `Result<JournalEntry>`; construction enforces the invariants below;
   - **posted entries are immutable** — the aggregate exposes read-only state and no post-construction mutators (corrections are reversals, never edits).
3. **`JournalLine`** — account id, signed `amount_minor` (`long`, +debit / −credit), transaction `CurrencyCode`, `base_amount_minor` (`long`), optional fx-rate metadata reference, optional `vat_code_id`/`business_partner_id`/`project_id` (nullable placeholders, un-modelled beyond ids — their catalogs are later WPs), optional attributions.
4. **Exact-integer balance invariant:** the aggregate cannot be constructed unless `SUM(base_amount_minor) == 0` across its lines, computed in **integer** arithmetic (no float/double/decimal anywhere). An entry needs ≥ 2 lines. Imbalance returns a domain failure (`journal_entry.unbalanced`). *(This is the pure-domain enforcement; the request-time eager 422 mapping + idempotency and the deferred DB trigger are WP05/WP02; the generative all-sequences property is WP08.)*
5. **Line attributions:** per line, optional `LineAttribution(userId, share_permille)`; `share_permille ∈ [1,1000]`; when a line carries attributions their sum must equal **1000‰** (`line_attribution.share_sum_invalid`); a line with no attributions is valid (per A3). No cross-line/derivation logic (that was an old UI concern).
6. **Reversal linkage:** `Reverse(reversalDate, newId, createdBy)` returns a **new** `Result<JournalEntry>` whose lines negate each original line's `amount_minor` and `base_amount_minor`, carrying `ReversesEntryId = original.Id`; the reversal is itself balanced by construction; reversing a reversal is permitted; the original aggregate is unchanged. Reversal date/period interaction per A2.
7. **Posting-validity port (salvage, fixture-pinned)** — pure evaluators mirroring the OLD `postingValidity.ts`, consuming plain reference snapshots (exactly the fixture input shape), returning a structured `PostingValidityError`/issue list; **currency policy is NOT here** (owned by ChartOfAccounts/WP03, composed in WP05):
   - `AssertPostingAccountsValid` — reasons `missing | inactive | future | expired`; `inactive` rejected in **both** purposes; `future`/`expired` window checks **business-only**; personal ignores windows.
   - `AssertPostingBusinessPartnersValid`, `AssertPostingUsersValid` — timebound refs: `inactive` rejected **personal-only** (a business `isActive:false` ref **passes**); `future`/`expired` business-only.
   - `AssertPostingProjectsValid` — `missing | future | expired` from start/end dates; no `inactive`, no purpose gating.
   - Error shape pinned: `PostingValidityError.issues[]` = `{ entityType, entityId, reason, txDate, source }`; multi-issue collection in input order.
8. **Period-state port + approved target divergence** — `GetEffectivePeriodState(txDate, periods) → open | closed | locked | no-period-defined`; `AssertPostingPeriodOpen(txDate, periods)` rejects `closed`/`locked` with `PeriodClosedError { periodName, state, txDate }`. Per A1, `no-period-defined` is also rejected for a posting: the OLD permissive fixture remains immutable evidence, but is classified as one deliberate target divergence and paired with an ADR/target-decision test.
9. **Golden-fixture consumer + structural unit tests** — load the manifest and consume all 37 posting/period cases: 36 must match the OLD oracle exactly; the one `assertPostingPeriodOpen`/no-period case must prove the documented A1 divergence rather than overwrite history. Add focused unit tests for the aggregate/balance/reversal/attribution rewrite behaviors.
10. **Architecture wiring** — Ledger.Domain enters the boundary assertions (`*.Domain` depends only on SharedKernel; EF confined to Infrastructure); add a `LeafLedger.Modules.Ledger.Tests` project to the solution and arch assembly set if required.

## Non-goals (explicitly deferred)

- **No application/endpoints/idempotency.** `POST /journal-entries`, `/reverse`, 422 mapping, the **eager balance check (N3 app half)**, idempotency middleware (N2), and period open/close/lock **write commands** are all **WP05**. This WP delivers pure domain only.
- **No currency policy.** Owned by ChartOfAccounts (WP03 `CurrencyPolicyEvaluator`); composed at the application layer in WP05. Ledger.Domain must **not** reference ChartOfAccounts.
- **No persistence.** No EF entity/config/`DbContext` change, no repositories, no migration, no mapping from the aggregate to the WP02 POCOs, no change to the P2-WP02 schema or the deferred balance trigger. Port-then-refactor (Part 4 §5); mapping is a later WP.
- **No FX arithmetic / revaluation / VAT.** FX-policy metadata resolution is WP03; rate lookup, conversion, rounding, revaluation, VAT calculation, and period-close postings are later WPs. This WP carries amounts already expressed as minor units and never computes a rate.
- **No property/invariant suite.** WP08 owns the generative "any command sequence ⇒ trial balance ≡ 0 / retried posts exactly-once" flagship; this WP proves invariants with targeted unit tests.
- **No new golden fixtures / no LL Fixture Smith task.** Posting-validity + period-state are fully pinned by the existing 37 P2-WP01 fixtures. Balance, reversal, immutability, and attribution have **no OLD oracle** (see Source material) and are pinned by unit + WP08 tests. Stated so QA does not flag a "missing fixtures" prerequisite.
- **No divergence from a golden fixture.** Any approved rule change (e.g. requiring a defined period per A1) requires an ADR/plan amendment and replacement expected artifacts, captured traceably; the developer must not improvise.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e` (the P2-WP01 capture SHA).

### Salvage verbatim in intent (fixture-pinned by P2-WP01)
- `src/shared/postingValidity.ts` — `evaluateAccountReference` (L96), `evaluateTimeboundReference` (L114), `assertPostingAccountsValid` (L134), `assertPostingBusinessPartnersValid` (L163), `assertPostingUsersValid` (L192), `assertPostingProjectsValid` (L221); error class `PostingValidityError` (L67).
- `src/shared/periodUtils.ts` — `getPeriodForDate` (L122), `getEffectivePeriodState` (L136); `assertPostingPeriodOpen` (L376) + `PeriodClosedError` (L353) in `postingValidity.ts`.
- Executable oracle: `tests/fixtures/golden/ledger-core/{posting-accounts,posting-business-partners,posting-users,posting-projects,period-state}/*.json` + `manifest.json`; provenance in `SOURCE.md`. **37 cases.**

### Reference only (not salvaged)
- `src/features/journal-entry/JournalEntryPage.tsx` — the OLD save flow (calls `assertPostingPeriodOpen`, currency policy, partner/account validity in sequence) and its **UI float balance** (`isBalanced`, `Math.abs(delta) < 0.01`). The ordering is a reference for WP05 composition; the float balance is **explicitly rejected** — the new invariant is exact-integer (P2-WP01 Non-goals).
- `handleDelete` (`JournalEntryPage.tsx` L517–553) — the OLD "correction" was a **hard delete** of the transaction + lines + attributions. This is the behavior the rebuild replaces with immutable reversal; it is **not** ported.

### Rewrite (spec-derived; no OLD oracle exists)
- **JournalEntry aggregate, exact-integer balance, immutability, reversal linkage** — Part 3 §3/§4. **Verified via OLD-repo search (2026-07-11): there is no reverse-entry function in `Lokkeccs/Accounting`** (subledger/period-close/accrual generators build *new* entries; corrections are deletes). Reversal is therefore greenfield, pinned by unit tests + the WP08 balance-preserving property, not by a captured fixture.
- **Line attribution 1000‰ invariant** — Part 3 §4.2 ("sum = 1000 enforced"). The OLD attribution logic is UI derivation (`deriveAttribution`/`rederiveLines`), not a pinned rule; only the sum invariant is carried.
- Module structure, typed ids, `Money`/minor-unit integer math, `Result`/`DomainError` construction — greenfield C# under the target architecture.

## Accounting decisions (LL Accounting Expert — REQUIRED before implementation)

**Resolved 2026-07-11 (LL Accounting Expert).** These are control-policy decisions grounded in the Swiss requirements for complete, truthful, systematic and verifiable records (Swiss Code of Obligations, **Art. 957a(2)**), retention/auditability (**Art. 958f**), and record integrity (**GeBüV Art. 3**). Swiss law does not prescribe LeafLedger's period-master or attribution data model; A1/A3 are explicit internal-control/product policies implementing those principles.

- **A1 — REQUIRE a defined open period.** Every new posting date must resolve to a defined period whose effective state is `open`; `no-period-defined`, `closed`, and `locked` all reject posting. This prevents unassigned postings and makes close controls deterministic. It deliberately diverges from the OLD backward-compat behavior (`no-period-defined ⇒ allowed`) pinned by one period fixture. **Do not alter that golden file.** The divergence is recorded in **[ADR-0005](../../architecture/adr/ADR-0005-posting-requires-open-period.md)** (accepted 2026-07-11); the fixture test must identify the OLD expected `ok` and prove the target rejection explicitly, linking back to ADR-0005. Bootstrap/onboarding must create an open period before first posting (route to WP05 if not already planned). Period-overlap prevention is not decided by A1 and must not be invented in this WP.
- **A2 — Chosen reversal date, always in an open period.** A reversal takes an explicit effective date; it must not silently backdate to the original entry date. The chosen date may equal the original date only while that period remains open. If the original period is closed/locked, post the reversal in the current/next appropriate open period and preserve `ReversesEntryId` plus the original reference for the audit trail. `AssertPostingPeriodOpen` therefore applies to reversals exactly as to ordinary postings. Reopening a closed period is a separate authorized period-management decision, not a side effect of reversal. This preserves the original record and traceable correction required by Art. 957a/958f and GeBüV Art. 3.
- **A3 — Attribution OPTIONAL, exact when present.** Swiss statutory bookkeeping does not require allocation of each journal line to internal users/owners. A line may have no attributions. If any are supplied, every share must be in `[1,1000]` and the per-line sum must equal exactly **1000‰**. No account kind is intrinsically attribution-mandatory in this WP. A future space/account policy may require attribution for selected lines, but that is a separately documented application rule; do not infer it here. Duplicate attribution to the same user should be rejected or normalized before aggregate construction so the 1000‰ allocation is unambiguous (record the exact choice in implementation notes).

*(Currency-policy target was already signed off in P2-WP03 A1; not reopened here.)*

## Golden fixtures

**Existing P2-WP01 fixtures are sufficient to document the salvaged surface; no Fixture Smith task is required.** The first implementation task wires the C# consumer and consumes all **37 existing cases**: **36 exact oracle matches + 1 documented A1 divergence** (`assertPostingPeriodOpen` with no defined period). The OLD golden file remains unchanged; an approved target-decision test/artifact pins the new rejection.

- posting-accounts (10): missing, inactive (both purposes), future/expired (business), personal-ignores-window, valid, validFrom/validTo boundaries, multi-issue.
- posting-business-partners (7) + posting-users (7): missing, inactive (personal-rejected vs business-allowed asymmetry), future/expired (business), personal-ignores-window, valid.
- posting-projects (5): missing, future, expired, valid-in-window, no-window-passes.
- period-state (8): `getEffectivePeriodState` ×4 + `assertPostingPeriodOpen` ×4 over open/closed/locked/no-period.

The test asserts the selected count is exactly **37 = 10 + 7 + 7 + 5 + 8** and fails on an unknown selected `unit`, preventing silent omissions. WP03 already consumed the other 22 (currency-policy + fx-metadata); the 59-case set is then fully consumed across WP03 + WP04.

## Dependencies

No new NuGet packages. BCL `System.Text.Json` for fixture loading; existing xUnit/test SDK. The `Domain` namespace adds no package to `LeafLedger.Modules.Ledger` (EF stays in `Infrastructure`).

## File list (implementation target)

**New — `backend/src/LeafLedger.Modules.Ledger/Domain/`**
- `Journal/JournalEntry.cs` (aggregate root + `JournalEntryTag : IEntityTag`; factory + `Reverse(...)`; immutability; balance invariant)
- `Journal/JournalLine.cs`
- `Journal/LineAttribution.cs`
- `Journal/JournalEntryStatus.cs` (enum, if a status is modelled)
- `PostingValidity/PostingValidityError.cs` (+ `PostingValidityIssue`, `PostingValidityReason` enum)
- `PostingValidity/PostingValidityEvaluator.cs` (accounts / business-partners / users / projects)
- `PostingValidity/References.cs` (plain input snapshots: `AccountReference`, `TimeboundReference`, `ProjectReference`, `PostingPurpose` enum)
- `Periods/PeriodState.cs` (enum) + `Periods/PeriodStateResolver.cs` + `Periods/PeriodClosedError.cs`
- Tiny enums/records may be split/combined without changing public behavior; record any deviation.

**New — `backend/tests/LeafLedger.Modules.Ledger.Tests/`**
- `LeafLedger.Modules.Ledger.Tests.csproj` (content-links the P2-WP01 golden JSON, as WP03 does)
- `Fixtures/LedgerCorePostingFixtureLoader.cs` (selects the 37 posting/period cases; guards units/dups/missing)
- `FixtureSelectionTests.cs` (asserts 37 = 10+7+7+5+8)
- `PostingAccountsGoldenFixtureTests.cs`, `PostingPartnersUsersGoldenFixtureTests.cs`, `PostingProjectsGoldenFixtureTests.cs`, `PeriodStateGoldenFixtureTests.cs`
- `JournalEntryBalanceTests.cs`, `JournalEntryReversalTests.cs`, `JournalEntryImmutabilityTests.cs`, `LineAttributionTests.cs`

**Modified**
- `backend/LeafLedger.sln` — add the Ledger.Tests project.
- `backend/tests/LeafLedger.ArchitectureTests/LeafLedger.ArchitectureTests.csproj` — already references the module (P2-WP02); no change expected unless the loader needs it.
- `backend/tests/LeafLedger.ArchitectureTests/ModuleBoundaryTests.cs` — Ledger is already in `AllLeafLedgerAssemblies()`; the `*.Domain`-only-SharedKernel rule now bites Ledger.Domain (verify the forbidden list covers ChartOfAccounts + EF + AspNetCore).
- `docs/rebuild/plans/P2-WP04-ledger-domain-journal-entry.md` — implementation notes/state.
- `docs/rebuild/status.md` — state/session log.

No files under `app/**`, `backend/openapi/**`, `backend/src/LeafLedger.Modules.Ledger/Infrastructure/**`, or migrations are changed.

## Boundary note (Domain → only SharedKernel)

Posting-validity evaluators take **plain reference snapshots** (id, isActive, validFrom/validTo, kind, start/end date) — exactly the fixture input shape — so Ledger.Domain does **not** reference ChartOfAccounts. `PostingPurpose` (Business|Personal) is defined locally in Ledger.Domain (ChartOfAccounts has its own `AppPurpose`; a SharedKernel consolidation of the purpose enum is a candidate later refactor, not this WP). This preserves `*.Domain depends only on SharedKernel`.

## Implementation sequence

1. Add the Ledger.Tests project + posting-fixture loader; prove exactly 37 relevant artifacts are discovered and parsed.
2. Port posting-validity (accounts → partners → users → projects) verbatim in intent; make all 29 posting fixtures green.
3. Port period-state (`getEffectivePeriodState` + `assertPostingPeriodOpen`) per A1; match 7 period fixtures exactly and classify the no-period assertion fixture as the documented OLD→target divergence, with an explicit target rejection test.
4. Add the `JournalEntry` aggregate + `JournalLine`/`LineAttribution`: exact-integer balance, attribution 1000‰ (per A3), immutability; focused unit tests.
5. Add `Reverse(...)` per A2 (negating lines, `ReversesEntryId`, balance-preserving); unit tests.
6. Wire architecture assertions; run Release build + relevant unit/architecture tests; document results and deviations; move to `verify`.

## Acceptance criteria (concrete tests)

1. **Build & boundaries:** `dotnet build LeafLedger.sln -c Release` = 0 warnings/0 errors. Architecture tests prove `LeafLedger.Modules.Ledger` **Domain** depends only on SharedKernel (no ChartOfAccounts, no `Microsoft.EntityFrameworkCore`, no `Microsoft.AspNetCore`); EF Core remains confined to `Infrastructure`.
2. **Fixture selection is complete:** a test loads `manifest.json`, selects the five posting/period folders, asserts **37 total = 10 + 7 + 7 + 5 + 8**, resolves every file, and fails on duplicate ids, missing files, or an unsupported selected unit.
3. **Posting-accounts fidelity:** all 10 pass — `missing | inactive` (both purposes) `| future | expired` (business), personal-ignores-window, valid, validFrom/validTo boundaries, and multi-issue collection; `issues[]` field names (`entityType/entityId/reason/txDate/source`) and input-order preserved. No message-text pinned.
4. **Business-partner & user fidelity:** all 14 pass — `missing`, `inactive` rejected personal-only, the **business `isActive:false` passes** asymmetry, `future`/`expired` business-only, personal-ignores-window, valid.
5. **Project fidelity:** all 5 pass — `missing | future | expired`, valid-in-window, no-window-passes; no `inactive` and no purpose gating exist.
6. **Period-state fidelity + A1 divergence:** all 8 fixtures are consumed — 7 match exactly; `getEffectivePeriodState` still returns `no-period-defined` when no period contains the date, but target posting validation rejects that result. The test preserves and identifies the OLD fixture's expected `ok`, then proves the approved target error. `closed`/`locked` retain `PeriodClosedError { periodName, state, txDate }`; the no-period error must have a distinct stable code (for example `posting_period.not_defined`) rather than pretending a named period is closed. The ADR/target-decision artifact is linked from the test/plan.
7. **JournalEntry aggregate + exact-integer balance:** a ≥2-line entry whose `base_amount_minor` sums to 0 constructs successfully; a non-zero sum returns `journal_entry.unbalanced`; balanced same-currency and multi-currency (base sums to 0 with differing transaction currencies) cases both pass; a reflection test proves **no public member exposes `float`/`double`/`decimal`** and balance uses integer math.
8. **Immutability + reversal:** the posted aggregate exposes no post-construction mutators; `Reverse(...)` returns a new balanced entry whose every line negates the original `amount_minor` and `base_amount_minor`, with `ReversesEntryId == original.Id`; reversing a reversal is allowed; the original is unchanged. `Reverse(...)` requires an explicit effective date and has no silent original-date default. The period evaluator separately proves that this date must resolve to a defined `open` period; WP05 composes that guard into posting orchestration.
9. **Attribution invariant:** a line whose attributions sum to 1000‰ is valid; a sum ≠ 1000 returns `line_attribution.share_sum_invalid`; each `share_permille` outside `[1,1000]` is rejected; a line with no attributions is valid (per A3).
10. **No persistence/contract drift:** `git diff --name-only` is limited to the file list above; no Infrastructure/migration/schema, Host, OpenAPI/generated client, workflow, or frontend changes; the existing Ledger integration suite (incl. `HasPendingModelChanges()`) stays green — the schema is untouched.
11. **Accounting sign-off traceability:** A1–A3 answers are recorded in this plan before implementation; the implementation matches the approved answers and the 37 fixtures, or an explicit ADR/fixture re-capture task blocks the WP. No accounting behavior is inferred by the developer.
12. **Quality gate:** all new Ledger.Domain tests + architecture tests pass in Release; fixture failures report the stable `id/unit/file`; domain coverage meets the repository target (≥90%) if coverage tooling is available (absence is not grounds to add a dependency here).

## Definition of done

A1–A3 recorded; the A1 divergence is recorded in [ADR-0005](../../architecture/adr/ADR-0005-posting-requires-open-period.md) (accepted); all 12 ACs pass; all 37 relevant golden fixtures are consumed (**36 exact + 1 explicit divergence**); balance/reversal/immutability/attribution unit tests green; Release build clean; architecture boundary green; no dependency, persistence, endpoint, or frontend changes. Then state → `verify` and route to LL QA Reviewer.

## Risks / notes

- **Scope boundary vs WP05.** WP04 = pure aggregate invariant (cannot construct an imbalanced/invalid entry). WP05 = request-time orchestration (eager 422 before COMMIT, idempotency, composing currency policy + posting-validity + period guard), plus period open/close/lock write commands. Keep the eager-check *orchestration* out of the domain.
- **Balance currency.** The invariant is `SUM(base_amount_minor) = 0` (reporting/base currency), matching the WP02 deferred trigger and Part 3 §4.2 — not transaction-currency balance. Transaction-currency amounts need not net to zero.
- **Reversal has no OLD oracle.** Confirmed by OLD-repo search; the reversal contract is spec-derived. Pin it with unit tests now and the WP08 property ("reverse(E) then E has zero net effect on every account balance"). Any richer reversal semantics require a plan amendment.
- **Attribution policy (A3 resolved).** Attribution is optional; when present it sums exactly to 1000‰. Any future mandatory-attribution policy belongs to a separately planned application rule.
- **Purpose enum duplication.** Ledger.Domain's `PostingPurpose` duplicates ChartOfAccounts' `AppPurpose` to preserve the boundary; note a possible SharedKernel consolidation for a later refactor (not this WP).
- **No float money.** This WP carries integer minor units only; no rate, conversion, or rounding is introduced.

## Implementation notes

- **2026-07-11 — LL Backend Dev:** User approved the plan after A1–A3 and ADR-0005. Created
   `P2-WP04-ledger-domain` from merged `origin/main` (`3f7fd34`). Added the pure Ledger Domain and
   fixture-first test project. The TDD red baseline failed with the planned missing `Domain` types;
   implementation then made all new tests green.
- Consumed exactly **37** selected P2-WP01 fixtures: accounts 10, partners 7, users 7, projects 5,
   period-state 8. The **36 unchanged** outcomes match the OLD oracle; `ps-no-period-allowed` preserves
   OLD expected `ok` and explicitly proves the ADR-0005 target rejection
   (`posting_period.not_defined`). Fixture files were not modified.
- Added immutable `JournalEntry`/`JournalLine`/`LineAttribution` domain types. Balance uses internal BCL
   `BigInteger` accumulation over public `long` minor-unit values to avoid both floating-point arithmetic
   and signed-overflow false balance. Reversal negates transaction/base minor units, preserves line
   metadata/attributions and original reference, links `ReversesEntryId`, requires an explicit date, and
   leaves the source aggregate unchanged.
- **A3 duplicate choice:** duplicate user attribution on one line is rejected with
   `line_attribution.duplicate_user` (not normalized). Optional empty attribution remains valid; a
   non-empty allocation must contain shares 1..1000 and sum exactly 1000‰.
- Plain cross-module/reference identifiers are `Guid` snapshots; only journal-entry identity uses
   `Id<JournalEntryTag>`. This avoids a ChartOfAccounts Domain dependency and matches persisted `uuid`
   values without inventing duplicate account/entity tags inside Ledger.
- File-list refinements: small period result/error records are combined in `PeriodState.cs`; shared
   golden assertions/test data are split into helper files. The Ledger test project mirrors the existing
   IntegrationTests direct `Microsoft.EntityFrameworkCore.Relational` 9.0.4 alignment reference to avoid
   the module's transitive 9.0.1/9.0.4 assembly warning; this introduces no package/version not already in
   the repository and no Domain dependency on EF.
- Architecture assertion is now module-specific: ChartOfAccounts.Domain forbids Ledger, and
   Ledger.Domain explicitly forbids ChartOfAccounts, EF Core, ASP.NET, and Host. No Infrastructure,
   migration, schema, Host, OpenAPI, workflow, or frontend files changed.
- **Final local evidence:** `dotnet build backend/LeafLedger.sln -c Release --no-incremental` = success,
   **0 warnings / 0 errors**. `dotnet test backend/LeafLedger.sln -c Release --no-build` = **176/176**:
   Ledger **58/58** (all 37 golden cases + 21 structural/rewrite tests), SharedKernel 45/45,
   ChartOfAccounts 33/33, architecture 3/3, Testcontainers PostgreSQL integration 37/37 including
   `HasPendingModelChanges()` (no schema drift). `git diff --check` clean.

## QA verdict

**PASS — 2026-07-11 (LL QA Reviewer).** Independently reproduced from a clean `--no-incremental` rebuild.

**Build & suite.** `dotnet build backend/LeafLedger.sln -c Release --no-incremental` = **0 warnings / 0 errors**. `dotnet test backend/LeafLedger.sln -c Release --no-build` = **176/176**: SharedKernel 45, ChartOfAccounts 33, **Ledger 58**, architecture 3, Testcontainers PostgreSQL integration 37 (incl. `HasPendingModelChanges()` → no schema drift). Targeted re-run by name: `FixtureSelectionTests` (37 = 10+7+7+5+8), all 8 `PeriodStateGoldenFixtureTests` (incl. the `ps-no-period-allowed` divergence), the 3-type no-float theory, `Reverse_requires_explicit_date_parameter`, and `Creates_balanced_entry_at_long_boundaries` = 14/14.

**AC-by-AC.** AC1 boundaries — arch 3/3; module-specific assertion proves `Ledger.Domain` forbids ChartOfAccounts/EF/ASP.NET/Host and EF stays confined to `Infrastructure`. AC2 selection — exact 37, guards duplicates/missing/unknown unit. AC3–AC5 posting-validity — accounts (inactive both purposes; window business-only; inclusive validFrom/validTo boundaries; multi-issue input order), partner/user (business `isActive:false` **passes** asymmetry; inactive personal-only; window business-only), projects (missing/future/expired, no inactive/purpose) all match the OLD oracle with pinned `issues[]` field names. AC6 — `GetEffectivePeriodState` still returns `no-period-defined`; posting validation rejects it with distinct `posting_period.not_defined`; the OLD golden `ok` is preserved and the ADR-0005 target rejection is proven in-test; closed/locked retain `PeriodClosedError{periodName,state,txDate}`. AC7 — exact-integer balance via `BigInteger` (overflow-safe at `long.MaxValue/MinValue`), no public `float/double/decimal`. AC8 — immutable aggregate; `Reverse(...)` returns a new balanced entry, negates both `amount_minor` and `base_amount_minor`, preserves line metadata + attributions + original reference, links `ReversesEntryId`, requires an explicit date (no default), leaves the original unchanged; reversing-a-reversal allowed. AC9 — attribution optional; 1..1000 range; per-line sum exactly 1000‰; duplicate user rejected. AC10 — `git diff` vs `origin/main` limited to sln + Ledger.csproj + `ModuleBoundaryTests.cs` + adr README + status (+ new Domain/tests/ADR-0005/plan); **golden fixtures byte-unchanged**; no Infrastructure/migration/schema/Host/OpenAPI/frontend/workflow. AC11 — A1–A3 recorded, ADR-0005 accepted, implementation matches the 37 fixtures. AC12 — all new tests green in Release.

**Financial integrity.** Money = integer minor units (`long`); balance summed in `BigInteger` (no float); immutability enforced (no setters, corrections via reversal); idempotency correctly deferred to WP05.

**Security.** No endpoints/persistence in scope → authorization correctly deferred to WP05/WP06; no secrets; construction validates at the factory boundary.

**Hallucination scan.** No behavior beyond the plan/ADR-0005/old oracle. The supplementary `posting_period.not_open` `DomainError` accompanies (does not replace) the fixture-pinned `PeriodClosedError`.

**Patch-layering scan.** No generic try/catch or self-heal in production domain. Guards are specific and named: `ArgumentNullException.ThrowIfNull` at boundaries; `journal_entry.amount_out_of_range` names the exact `long.MinValue` negation edge; the fixture loader throws specific `InvalidDataException`/`FileNotFoundException`.

**Non-blocking carry-forwards** (do not block this WP): (1) standardize the ProblemDetails error-code taxonomy in WP05 — `posting_period.not_open` is currently internal-only and unpinned; (2) period boundary inclusivity (`<= endDate`) is consistent with the calendar-date fixtures but not endpoint-pinned — WP08 should pin exact period boundary semantics; (3) `description` null/empty validation is not enforced in the aggregate — appropriate as a WP05 request-boundary concern.

**Verdict:** PASS → **done**. Cleared for LL Git.
