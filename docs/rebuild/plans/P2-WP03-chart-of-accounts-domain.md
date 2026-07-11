# P2-WP03 — Chart of Accounts domain: accounts, groups, currency policy, FX-policy resolution

- **Phase:** 2 (ledger core)
- **State:** done — QA PASS 2026-07-11 (all 10 ACs independently reproduced; 118/118)
- **Owner (implementation):** LL Backend Dev
- **Depends on:** P2-WP01 (22 relevant golden fixtures: 11 currency-policy + 11 FX-policy/metadata), P2-WP02 (accounts/groups schema + `int4range` overlap wall), P1-WP03 (`CurrencyCode`, typed ids/results).
- **Blocks:** P2-WP04 (Ledger domain consumes account catalog/policies), P2-WP05 (posting application composes currency/FX checks), P2-WP08 (financial-invariant suite).
- **Estimated size:** ≤ 2 days (pure domain module + fixture consumer + unit tests; no persistence refactor/endpoints).
- **Spec sources:** `docs/architecture/rebuild/03-target-architecture.md` §3 (Chart of Accounts owns accounts, groups, code ranges, ownership, FX policy), §4.1–4.2 (`int4range` groups, integer money/currency), §5 (module/Domain boundaries); `docs/architecture/rebuild/04-implementation-plan.md` Phase 2 + §5 (currency/FX semantics are **salvage**, data access is **rewrite**, port and refactor separately); `docs/architecture/rebuild/05-quality-and-maintainability.md` §1.1/§1.3 (pure-domain coverage and golden-master gate).

## Goal

Create the pure C# **Chart of Accounts domain** that represents accounts and account groups and reproduces the OLD currency-policy and FX-policy behavior against the P2-WP01 golden fixtures. The deliverable is authoritative, persistence-free domain code that P2-WP04/P2-WP05 can consume without referencing EF Core or the anemic WP02 persistence POCOs.

This WP deliberately separates the **behavioral port** from the later persistence/application refactor: fixtures must be green before repositories, CRUD endpoints, or `LedgerDbContext` ownership are changed.

## Scope

1. **New module boundary:** `LeafLedger.Modules.ChartOfAccounts` with a pure `Domain` namespace and no EF/AspNetCore dependency.
2. **Account model:**
   - typed `AccountKind` = `asset | liability | equity | income | expense`;
   - account identity, group identity, integer code, name, `CurrencyCode`, active flag, optional inclusive `DateOnly` validity bounds, optional FX override;
   - construction rejects blank name, invalid currency, and `validFrom > validTo` through existing `Result`/`DomainError` conventions;
   - no posting-date decision here (active/window semantics belong to P2-WP04's posting-validity port).
3. **Account-group model:** identity, name, optional parent, `AccountCodeRange`, optional FX defaults.
   - `AccountCodeRange` is an inclusive domain range (`start` and `end` both valid), matching the OLD definitions/suggestion loop; creation rejects `start > end`; `Contains(code)` includes both boundaries.
   - future persistence mapping must convert inclusive `[start,end]` to PostgreSQL `int4range [start,end+1)`; this WP does not reference Npgsql.
   - group-to-group overlap remains DB-enforced by the P2-WP02 GiST exclusion constraint; no in-memory catalog-wide duplicate wall is invented.
   - an account code is **not** rejected merely because it is outside its group's range: in the OLD system the range is a suggestion aid, and the target spec requires non-overlapping group ranges but does not state membership enforcement.
4. **Currency policy port:** effective policy derives from account kind exactly as pinned: income/expense → `any`; asset/liability/equity → `fixed`; comparison trims and uppercases; mismatches accumulate in input order as `currency-not-allowed`; missing accounts and empty currencies are skipped in this policy unit because other command validation owns those errors.
5. **FX-policy port:** typed `FxTreatment`, `FxRateTiming`, and `VatFxMethod`; group inference; partial group override; partial account override; precedence `account override > group override > inferred fallback`; personal/business defaults; transaction-line metadata with `DateOnly` rate date and account currency.
6. **Golden-fixture consumer:** load the existing manifest, select all and only the 11 `currency-policy/*` + 11 `fx-metadata/*` cases, and execute them against the C# domain. Test adapters may deterministically translate fixture integer ids to typed ids and back for comparison; production domain ids remain UUID-backed.
7. **Architecture wiring:** add the module and its test project to the solution and architecture assembly set; prove `*.Domain` has no EF/AspNetCore dependency.

## Non-goals

- No CRUD endpoints, OpenAPI changes, authorization, idempotency, RLS binding, application handlers, repositories, or frontend TS mirror.
- No EF entity/configuration move, no new `DbContext`, no migration, and no change to the P2-WP02 schema. The baseline `LedgerDbContext` remains the single migration owner for now; mixing a persistence re-home into the fidelity port would violate Part 4 §5's port-then-refactor rule. ADR-0004/context ownership remains a later docs/refactor decision.
- No posting-validity guard (`missing/inactive/future/expired`) and no period rule. Those consume P2-WP01's account/period fixtures in P2-WP04/P2-WP05.
- No journal aggregate, balance/reversal/attribution logic, posting command, or transaction-line persistence.
- No built-in business/personal chart seed catalog, cash-flow classification, cash-equivalent inference, account-code suggestion, import validation, IBAN, ownership, investment roles, or group membership mutation. Those old UI/data behaviors are not covered by the P2-WP01 oracle and are not required by this WP title/spec.
- No FX-rate lookup, conversion, rounding, revaluation engine, VAT calculation, or period-close behavior. This WP resolves **policy metadata only**; money-bearing behavior lands in later WPs.
- No unpinned helpers such as OLD `shouldForceHistoricalForInvestmentGroup` UI enforcement or ambiguous-group warnings. Adding them requires fixtures and accounting sign-off in a separate WP/plan amendment.
- No deliberate divergence from a golden fixture. Any approved rule change requires an ADR/plan amendment and replacement expected artifacts captured traceably; LL Backend Dev must not improvise.

## Source material — salvage vs rewrite

### Salvage verbatim in intent (fixture-pinned)

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

- `src/shared/postingValidity.ts`:
  - `CurrencyPolicyError` L287;
  - `resolveEffectiveCurrencyPolicy` L302–309;
  - `assertPostingCurrencyPolicyValid` L313–344.
  - Behavior: P&L any; balance-sheet fixed; trimmed/case-insensitive comparison; missing/empty skips; ordered multi-issue error.
- `src/shared/fxPolicy.ts`:
  - policy enums/interfaces L1–18;
  - group sets L27–55;
  - `inferBusinessFxPolicy` L78–128;
  - personal/default policy L130–151;
  - `resolveGroupFxPolicy` L153–166;
  - `resolveAccountFxPolicy` L168–188;
  - `buildTransactionLineFxMetadata` L190–214.
  - Behavior: group inference + field-by-field overrides; account beats group; personal fallback historical/no-revalue; metadata preserves account currency and calendar date.
- Old seed tests: `tests/accountCurrencyPolicy.test.ts`; `tests/fxPolicy.investmentsOrDefaults.test.ts`.
- Executable oracle: `tests/fixtures/golden/ledger-core/manifest.json`, `currency-policy/*.json`, `fx-metadata/*.json`; provenance and formats documented in `SOURCE.md` and `fixture-format.md`.

### Reference only (not salvaged in this WP)

- `src/data/db.ts` `Account` L43–82 and `AccountGroup` L928–958: field-coverage checklist only; Dexie shape and numeric ids are not architecture guidance.
- `src/shared/accountGroups.ts` L3–9 (`AccountCodeRange`) and `suggestNextAccountCode` around L196–209: establish that OLD ranges are inclusive. Built-in group lists and suggestion behavior are not ported.
- `src/features/accounts/view-model/useAccountsViewModel.ts` around L532 and L808–860: UI/import enforcement for investment-group historical treatment. This is intentionally excluded because it is not fully pinned by the P2-WP01 fixture set.

### Rewrite

- Module structure, typed UUID identities, `CurrencyCode` integration, `Result`/`DomainError` construction, and all future persistence/application code are greenfield C# design under the target architecture.
- No Dexie/DataApi code, data migration, or old mutable persistence model is ported.

## Golden fixtures

**Existing fixtures are sufficient for this WP's salvaged rule surface; no Fixture Smith task is required.** The first implementation task is to wire the C# fixture consumer and make the following **22 existing cases** executable:

- 11 currency-policy cases: income/expense any; asset match/mismatch; liability/equity mismatch; case-insensitive; missing/empty skips; ordered multi-issue collection.
- 11 FX cases: monetary/historical/default asset/default liability; account override; personal fallback; date normalization; group fallback/override; account fallback/override.

The test must assert the selected manifest count is exactly 22 and fail on an unknown selected `unit`, preventing silent omissions when fixtures evolve. P2-WP04/P2-WP05 consume the remaining fixture folders.

## Accounting decisions (LL Accounting Expert — 2026-07-11)

### A1 — Currency-policy target: approved with an orchestration condition

Adopt the OLD rule as the target **account transaction-currency policy**:

- income/expense accounts accept any transaction currency;
- asset/liability/equity accounts are denominated in one fixed account currency;
- comparison is normalized and case-insensitive.

This is a sound chart-of-accounts policy, not a rule that foreign-currency P&L amounts escape translation: every posting must still be translated into the space's base/reporting currency and balanced in integer base minor units in P2-WP04/P2-WP05. Swiss CO permits accounting in the national currency or the currency essential to the business and requires foreign-currency values to be translated with the method disclosed (OR Art. 958d para. 3); the fixed account currency supplies the denomination, while the journal line retains transaction and base amounts.

Keep empty transaction currency as a **policy-unit no-op** exactly as the fixture pins, but only because this evaluator is compositional. The posting command/API must reject an absent or invalid currency before persistence in P2-WP05 (`CurrencyCode` required). “Skipped by this evaluator” must never mean “valid posting.” Missing accounts remain owned by P2-WP04's reference validation.

### A2 — FX-policy target and override limits: defaults approved; universal investment lock rejected

Approve the OLD matrix as the **default inference matrix**, with account-over-group precedence:

- monetary groups → transaction-date recognition + closing-rate revaluation;
- historical-cost groups → transaction-date recognition + no routine closing-rate revaluation;
- commodities/current-value groups → valuation-date/current-value treatment where a reliable observable market price exists and that valuation basis is elected consistently;
- unknown liabilities → monetary by default; other unknown kinds → historical by default;
- personal spaces → historical/no-revaluation as a non-statutory product default;
- precedence remains `account override > group override > inferred fallback`, field by field, as pinned by the 11 FX fixtures.

**Investment historical treatment may be overridden.** Do **not** port the OLD UI helper that universally forces `Financial Investments`/`Investments` to historical treatment. That label can contain economically different instruments:

- participations and non-monetary investments normally remain at acquisition/production cost less required impairment under OR Art. 960a;
- foreign-currency monetary instruments (for example receivables or debt instruments whose return is a fixed/determinable currency amount) require monetary FX treatment and closing-rate remeasurement under accepted accounting practice;
- assets with an observable market price may be measured at that price under the election in OR Art. 960b, applied consistently with the required valuation reserve/disclosures; current-value treatment may therefore be appropriate for quoted securities or commodities.

Accordingly, “Financial Investments → historical” is a conservative **default**, not an invariant. An account-level override is permitted when the instrument classification and documented valuation policy justify it. Group defaults may be overridden for a homogeneous group, but account override remains necessary for mixed groups.

The resolver may reproduce the fixture-pinned field-by-field merge in this WP. It must not claim that every mechanically constructible combination is legally appropriate. Enforcement of valuation-policy evidence, consistent election across like assets, impairment, closing-rate execution, reserve/disclosure, and period-end postings belongs to the later account-maintenance/revaluation/application WPs. Add this as a documented carry-forward; do not invent evidence fields or FX arithmetic in P2-WP03.

**Regulatory basis:** OR Art. 958c (proper accounting principles, including consistency/reliability), Art. 958d para. 3 (reporting currency and foreign-currency translation disclosure), Art. 960 and 960a (individual/prudent valuation and acquisition-cost ceiling/impairment), and Art. 960b (optional observable-market-price valuation and related reserve/disclosure). Swiss GAAP FER foreign-currency practice likewise distinguishes monetary closing-rate treatment from historical-rate treatment for non-monetary historical-cost items.

### Fixture impact

No P2-WP01 fixture change is required. The fixtures pin the approved defaults and override precedence; they do not pin the excluded “force every investment group to historical” UI helper. P2-WP03 remains within scope. Carry forward the legal/evidence enforcement noted above to the WP that introduces account-maintenance commands and to the FX revaluation WP.

## Dependencies

No new NuGet packages. Use the BCL (`System.Text.Json`) for fixture loading and existing xUnit/test SDK packages. The new domain project references only `LeafLedger.SharedKernel`.

## File list (implementation target)

**New — `backend/src/LeafLedger.Modules.ChartOfAccounts/`**
- `LeafLedger.Modules.ChartOfAccounts.csproj`
- `Domain/Accounts/Account.cs`
- `Domain/Accounts/AccountKind.cs`
- `Domain/Groups/AccountGroup.cs`
- `Domain/Groups/AccountCodeRange.cs`
- `Domain/CurrencyPolicy/CurrencyPolicy.cs`
- `Domain/CurrencyPolicy/CurrencyPolicyIssue.cs`
- `Domain/CurrencyPolicy/CurrencyPolicyEvaluator.cs`
- `Domain/Fx/FxPolicy.cs`
- `Domain/Fx/FxPolicyOverride.cs`
- `Domain/Fx/FxPolicyResolver.cs`
- `Domain/Fx/TransactionLineFxMetadata.cs`
- Additional tiny enum files may be split/combined without changing public behavior; record any deviation.

**New — `backend/tests/LeafLedger.Modules.ChartOfAccounts.Tests/`**
- `LeafLedger.Modules.ChartOfAccounts.Tests.csproj`
- `Fixtures/LedgerCoreFixtureLoader.cs`
- `CurrencyPolicyGoldenFixtureTests.cs`
- `FxPolicyGoldenFixtureTests.cs`
- `AccountCodeRangeTests.cs`
- `AccountTests.cs`
- `AccountGroupTests.cs`

**Modified**
- `backend/LeafLedger.sln` — add domain + test projects.
- `backend/tests/LeafLedger.ArchitectureTests/LeafLedger.ArchitectureTests.csproj` — reference/load the new module if required by the current assembly-loading pattern.
- `backend/tests/LeafLedger.ArchitectureTests/ModuleBoundaryTests.cs` — include `LeafLedger.Modules.ChartOfAccounts` in `AllLeafLedgerAssemblies()`.
- `docs/rebuild/plans/P2-WP03-chart-of-accounts-domain.md` — implementation notes/state.
- `docs/rebuild/status.md` — state/session log.

No files under `app/**`, `backend/openapi/**`, `backend/src/LeafLedger.Modules.Ledger/Infrastructure/**`, or migrations are changed.

## Implementation sequence

1. Add the domain/test projects and fixture loader; prove exactly 22 relevant P2-WP01 artifacts are discovered and parsed.
2. Port currency policy verbatim in intent; make all 11 currency fixtures green.
3. Port FX policy/metadata verbatim in intent; make all 11 FX fixtures green.
4. Add the minimal Account/AccountGroup/AccountCodeRange structural model and focused unit tests; do not add posting or catalog-wide rules.
5. Wire architecture tests; run Release build + relevant unit/architecture tests; document results and any deviations; move to `verify`.

## Acceptance criteria (concrete tests)

1. **Build and boundaries:** `dotnet build LeafLedger.sln -c Release` has 0 warnings/errors. Architecture tests include `LeafLedger.Modules.ChartOfAccounts` and prove Domain has no EF Core/AspNetCore dependency; the domain project references only SharedKernel/BCL.
2. **Fixture selection is complete:** a test loads `manifest.json`, selects `currency-policy/*` and `fx-metadata/*`, asserts **22 total = 11 + 11**, resolves every file, and fails on duplicate ids, missing files, or an unsupported selected unit.
3. **Currency-policy fidelity:** all 11 currency fixtures pass byte-for-structure after normalizing typed ids back to fixture ids. Tests prove `any` vs `fixed`, trimmed/case-insensitive comparison, missing/empty skips, issue field names/reason, issue ordering, and multi-issue collection. No exception-message text is pinned.
4. **FX-policy fidelity:** all 11 FX fixtures pass. Tests prove business group inference, unknown-kind fallbacks, personal fallback, field-by-field group/account override precedence, all policy fields, `DateOnly` rate date, and unchanged `fxCurrency` value expected by the fixture.
5. **Account range semantics:** tests prove inclusive range boundaries, `Contains` true at start/end and false outside, and `start > end` returns a domain failure. No Npgsql type leaks into Domain.
6. **Account structural invariants:** tests prove blank name, invalid currency, and `validFrom > validTo` fail explicitly; valid account construction preserves kind/code/currency/active/window/optional FX override. There is no float/double/decimal amount member.
7. **Group structural invariants:** tests prove blank group name and invalid range fail; a valid group preserves parent/range/optional FX defaults. Tests explicitly show that range containment is queryable but not automatically used to reject account membership (no unapproved rule).
8. **No persistence or contract drift:** `git diff --name-only` is limited to the file list above; no migrations/schema, Ledger Infrastructure, Host, OpenAPI/generated client, workflows, or frontend changes; `HasPendingModelChanges()` remains false if the existing Ledger schema test is run.
9. **Accounting sign-off traceability:** A1–A2 answers are recorded in this plan before implementation; implementation matches the approved answer and existing fixtures, or an explicit ADR/fixture re-capture task blocks the WP. No accounting behavior is inferred by the developer.
10. **Quality gate:** all new ChartOfAccounts tests and architecture tests pass in Release; fixture failures report the stable fixture id/unit/file so drift is diagnosable. Domain coverage meets the repository target (≥90%) if coverage tooling is available; absence of tooling is not grounds to add a dependency in this WP.

## Definition of done

A1–A2 recorded; all 10 ACs pass; 22/22 relevant golden fixtures green; Release build clean; architecture boundary green; no dependency, persistence, endpoint, or frontend changes. Then state → `verify` and route to LL QA Reviewer.

## Risks / notes

- **Schema/domain ownership:** WP02's single `LedgerDbContext` is intentionally not split here. Combining a context/migration re-home with the behavior port would obscure fidelity and risks duplicate migration ownership. Resolve ADR-0004 and persistence ownership as a separate approved refactor once the domain is green.
- **Inclusive vs PostgreSQL range:** OLD domain ranges include both endpoints; PostgreSQL canonical integer ranges are naturally half-open. Keep the domain inclusive and test future conversion to `[start,end+1)` at the infrastructure boundary; do not leak Npgsql into Domain.
- **Fixture permissiveness:** missing accounts/empty currencies are skipped by the currency-policy unit because OLD orchestration validates them elsewhere. P2-WP05 must enforce command required fields and P2-WP04 handles missing account references; do not reinterpret the fixture as permission to accept malformed API input.
- **Investment enforcement resolved (A2):** historical treatment is the conservative default for investment groups, not a universal invariant. Account/group override is permitted when instrument classification and documented valuation policy justify it. The unpinned OLD force helper remains excluded. Later account-maintenance/revaluation WPs must enforce evidence, consistency, impairment, and OR Art. 960b reserve/disclosure requirements.
- **No float money:** this WP carries no amounts. FX rates/conversion/rounding are explicitly later work; policy metadata alone must not introduce money arithmetic.

## Implementation notes

- **2026-07-11 — implemented; state → verify (LL Backend Dev).**
  - Created branch `P2-WP03-chart-of-accounts-domain` from merged `origin/main` (`32fc5a7`) after preserving/restoring the planning docs. User approval and A1–A2 accounting sign-off were recorded before code changes.
  - **TDD baseline:** added the test project/fixture loader and structural tests first; the initial run failed as expected with the missing module/project and 33 compile errors. Added production code only after that red baseline.
  - Added pure `LeafLedger.Modules.ChartOfAccounts` (only project reference: SharedKernel; **0 NuGet packages**): typed account/group ids, `AccountKind`, validated `Account`, validated inclusive `AccountCodeRange`, `AccountGroup`, currency-policy evaluator, FX policy/defaults/field-level overrides, and `DateOnly` transaction-line metadata. No EF/AspNetCore/Npgsql dependency.
  - **Fixture fidelity:** test content-links the existing P2-WP01 JSON (no copied/changed artifacts). Loader filters only `currency-policy/*` + `fx-metadata/*`, rejects unknown selected units, duplicate ids, or missing files, and asserts **22 = 11 + 11**. Golden run: **22/22 PASS**; structural/selection run: **11/11 PASS**; total new tests **33/33 PASS**.
  - Currency evaluator intentionally accepts raw nullable currency text rather than `CurrencyCode`: this is required to reproduce the fixture-pinned policy-unit behavior (trim/case-fold and empty-currency skip). The `Account` aggregate itself requires a supported `CurrencyCode`; P2-WP05 still owns command-level required-currency validation per A1.
  - FX implementation reproduces only fixture-pinned resolver behavior. The unpinned OLD investment-force helper is absent, per A2: historical is a default and justified overrides remain possible. No FX rates, money arithmetic, revaluation, evidence fields, or legal-disclosure logic added.
  - **File-layout deviations:** tiny related enums/records were consolidated into `FxPolicy.cs`; `AccountTag`/`AccountGroupTag` live beside their entities. Public behavior/file scope matches the plan; no new dependency.
  - Architecture assembly set now includes ChartOfAccounts; the Domain boundary also explicitly rejects a dependency on `LeafLedger.Modules.Ledger`. Solution includes module + test projects.
  - **Actual gates:** `dotnet build LeafLedger.sln -c Release` = **0 warnings / 0 errors**; full `dotnet test LeafLedger.sln -c Release --no-build` with Docker = **118/118 PASS** (**SharedKernel 45 + ChartOfAccounts 33 + architecture 3 + integration 37**). Existing integration suite includes `HasPendingModelChanges()` and remained green, proving no schema/model drift. Editor diagnostics: none. `git diff --check`: clean.
  - Scope audit: only new ChartOfAccounts source/tests + solution/architecture wiring + this plan/status. No Ledger Infrastructure, migration, Host, endpoint, OpenAPI/generated client, workflow, or frontend change.

## QA verdict

**PASS — 2026-07-11 (LL QA Reviewer).** All 10 acceptance criteria independently reproduced from a clean `--no-incremental` rebuild on branch `P2-WP03-chart-of-accounts-domain` (based on merged `origin/main` `32fc5a7`, ancestry confirmed). No application code modified by QA.

**Reproduced gates (actual):**
- `dotnet build LeafLedger.sln -c Release --no-incremental` = **0 warnings / 0 errors** (warnings-as-errors + analyzers on).
- `dotnet test … --filter "Category!=Integration"` = SharedKernel **45/45** + ChartOfAccounts **33/33** + Architecture **3/3** = **81/81**.
- `dotnet test … --filter "Category=Integration"` (Docker/Testcontainers `postgres:17`) = **37/37** — includes the `HasPendingModelChanges()` check → **no Ledger model/schema drift**.
- **Total 118/118.** ChartOfAccounts split independently confirmed: golden **22/22** (`~GoldenFixture`) + structural/loader **11/11** (`!~GoldenFixture`).

**AC-by-AC evidence:**
1. **Build & boundaries — PASS.** Release 0/0. `ModuleBoundaryTests.AllLeafLedgerAssemblies()` loads `LeafLedger.Modules.ChartOfAccounts`; `DomainNamespacesDependOnlyOnSharedKernel` forbids `Host`/`Modules.Ledger`/`EntityFrameworkCore`/`AspNetCore` (3/3 green). `.csproj` references only `..\LeafLedger.SharedKernel` and 0 NuGet packages (verified).
2. **Fixture selection — PASS.** `FixtureSelectionTests` asserts 22 = 11 + 11; loader rejects duplicate ids (`InvalidDataException`), missing files (`FileNotFoundException`), and unsupported units (`InvalidDataException`).
3. **Currency fidelity — PASS.** 11/11. `CurrencyPolicyGoldenFixtureTests` asserts `any` vs `fixed`, ordered multi-issue collection, and each issue's `accountId`/`accountCurrency`/`txCurrency`/`reason`/`source` after id round-trip; evaluator `Normalize` = trim + `ToUpperInvariant`, empty/missing skip — matches old `assertPostingCurrencyPolicyValid`. No message text pinned.
4. **FX fidelity — PASS.** 11/11. `FxPolicyResolver` group sets + `InferBusiness` order (current-value → monetary → historical → liability-monetary / else-historical) and field-by-field `Apply` (account > group > inference) reproduce old `fxPolicy.ts`; date compared via `CultureInfo.InvariantCulture`; `fxCurrency` preserved.
5. **Range semantics — PASS.** `AccountCodeRangeTests`: inclusive `Contains` at 1000/1099, false at 999/1100; `start > end` → `account_code_range.invalid`. No Npgsql in Domain.
6. **Account invariants + no float — PASS.** Blank name / invalid currency (delegates to `CurrencyCode.TryParse`) / reversed window rejected explicitly; `Public_model_exposes_no_floating_amount_type` reflection test green; grep confirms no `float`/`double`/`decimal`/`Single` anywhere in module source.
7. **Group invariants — PASS.** Blank name → `account_group.name_required`; valid group preserves parent/range/FX defaults; `Valid_account_preserves_…_without_enforcing_group_range` (code 9999 in unrelated group) proves containment is queryable but not enforced.
8. **No persistence/contract drift — PASS.** `git diff --name-only origin/main` limited to `LeafLedger.sln`, arch `.csproj`, `ModuleBoundaryTests.cs`, `status.md`; untracked = the module + test project + this plan only. No `app/**`, `openapi/**`, Ledger `Infrastructure/**`, migration, Host, or workflow change. Integration `HasPendingModelChanges()` green.
9. **Accounting traceability — PASS.** A1–A2 recorded pre-implementation; currency evaluator empty-skip is compositional with the P2-WP05 command guard (A1); FX historical is a default, not a lock (A2). Hallucination scan: the unpinned `shouldForceHistoricalForInvestmentGroup` helper is **absent** (grep = no matches).
10. **Quality gate — PASS.** All new + arch tests green in Release; `LedgerCoreFixture.ToString()` surfaces `id (unit, file)` for drift diagnosis.

**Financial integrity:** module carries no amounts; no float/decimal money type; `Account` requires a supported `CurrencyCode`; immutability/idempotency out of scope for a pure catalog domain (owned by P2-WP04/WP05).

**Security:** no secrets; no endpoints/RLS introduced; fixture loader reads only content-linked repo test data via `AppContext.BaseDirectory`.

**Patch-layering scan:** no `try/catch` in production domain code; the only `throw`s are the loader's specific `InvalidDataException`/`FileNotFoundException` naming the exact failure — no broad masking.

**Non-blocking carry-forwards (already recorded in plan, not defects):** (a) P2-WP05 must reject absent/invalid transaction currency and translate to base minor units (OR 958d(3)); (b) FX evidence/consistency/impairment/OR 960b reserve & disclosure enforcement belongs to account-maintenance + revaluation WPs; (c) inclusive-range → `int4range [start,end+1)` conversion to be tested at the future Infrastructure boundary.

→ **State `done`.** Cleared for LL Git (branch `P2-WP03-chart-of-accounts-domain`).
