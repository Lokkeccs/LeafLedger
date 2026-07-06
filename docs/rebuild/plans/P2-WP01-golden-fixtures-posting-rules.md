# P2-WP01 — Golden fixtures: ledger-core posting rules (posting-validity, currency-policy, period-state, FX-policy metadata)

- **Phase:** 2 (ledger core)
- **State:** planned (awaiting approval)
- **Owner (implementation):** LL Fixture Smith (fixtures only; no application code, no C# domain)
- **Depends on:** P1-WP03 (SharedKernel — for the eventual consumers; not needed to author fixtures). No dependency on the Postgres schema.
- **Blocks:** P2-WP03 (ChartOfAccounts port), P2-WP04 (Ledger domain port), P2-WP05 (posting endpoints) — all must port against these fixtures.
- **Estimated size:** ≤ 2 days (mechanical extraction; old unit tests already exist as seed cases).
- **Spec sources:** `docs/architecture/rebuild/04-implementation-plan.md` §"Phase 2" ("posting rules ported from `postingValidity.ts`/`fxPolicy.ts` semantics, float tolerances → exact integer math") + §5 fidelity protocol + recommendation #4 ("Golden fixtures are the spine … build them before porting anything"); `docs/architecture/rebuild/05-quality-and-maintainability.md` §1.1 ("Golden masters … executed against BOTH old TS and new C# domain — the porting oracle"); `docs/architecture/rebuild/06-feature-roadmap.md` M0 ("Golden fixtures — built early").

## Goal
Produce the **porting-oracle fixture set** for the Phase-2 ledger-core posting rules: capture exact input→outcome artifacts from the OLD running implementation so that the new C# domain (and the thin TS pre-validation mirror) can be verified to reproduce old behavior **to the branch**. This is the "spine" deliverable that every subsequent Phase-2 porting WP is graded against. No behavior is invented or changed here — this WP only *pins* what the old code does.

## Scope (what this WP delivers)
Language-neutral golden fixtures under `tests/fixtures/golden/ledger-core/`, captured by executing the OLD implementation, for these pure units from the old repo (`Lokkeccs/Accounting`, read-only reference):

1. **Posting validity** — `src/shared/postingValidity.ts`:
   - `assertPostingAccountsValid` — reasons `missing | inactive | future | expired`; business honors `validFrom/validTo` windows, personal ignores windows but still rejects `inactive` (`evaluateAccountReference`).
   - `assertPostingBusinessPartnersValid`, `assertPostingUsersValid` — `evaluateTimeboundReference` (missing/inactive; future/expired business-only).
   - `assertPostingProjectsValid` — `missing | future | expired` from `startDate/endDate` (no purpose gating; no `inactive`).
   - `assertPostingCurrencyPolicyValid` — `resolveEffectiveCurrencyPolicy`: income/expense = **any**, asset/liability/equity = **fixed** to account currency; case-insensitive compare; skips missing accounts and empty currencies; collects multiple issues.
   - `assertPostingPeriodOpen` — `closed | locked` → `PeriodClosedError`; `no-period-defined` → **allowed** (backward-compat).
   - **Error-shape pinning**: `PostingValidityError.issues[]` (`{entityType, entityId, reason, txDate, source}`), `CurrencyPolicyError.issues[]` (`{accountId, accountCurrency, txCurrency, reason, source}`), `PeriodClosedError` (`{periodName, state, txDate}`).
2. **Period state** — `src/shared/periodUtils.ts` `getEffectivePeriodState(txDate, periods)` → `open | closed | locked | no-period-defined` (the primitive `assertPostingPeriodOpen` sits on; periods open/close/lock is Phase-2 scope).
3. **FX-policy metadata** — `src/shared/fxPolicy.ts`:
   - `resolveGroupFxPolicy` (the `MONETARY_GROUPS` / `HISTORICAL_GROUPS` → treatment matrix),
   - `resolveAccountFxPolicy` (account override ?? group fallback),
   - `buildTransactionLineFxMetadata(purpose, account, txDate)` → `{fxRateDate (midnight-normalized), fxRateTiming, fxTreatmentApplied, fxClosingRevalueApplied, fxVatMethodApplied, fxCurrency}`.
4. **Provenance + capture harness** — a `SOURCE.md` recording the exact old-repo commit SHA and per-unit file/line refs, and a small committed capture script/harness (run against the old repo) so the artifacts are **reproducibly re-capturable**, not hand-authored.
5. **Fixture format schema** — one documented JSON shape (`input` + `expected`, where `expected` is `{ ok: true }` or `{ error: { type, … } }`), language-neutral, loadable by both the C# domain tests and the TS mirror tests.

## Non-goals (explicitly deferred)
- **No porting.** No C# domain, no TS mirror, no wiring these fixtures into a test runner — that happens in P2-WP03/WP04/WP05, each of which loads this set.
- **No balance-invariant fixtures.** The old balance check is UI-side float (`Math.abs(delta) < 0.01` in `JournalEntryPage.tsx`) and is **not** a salvage unit. The new balance invariant is exact-integer, **DB-enforced** (deferred constraint trigger) and pinned by the property/invariant suite (P2-WP08), not by an old float function. Stated so QA does not flag a "missing balance fixture."
- **No VAT, FX-revaluation, or period-close fixtures.** `fxRevaluationEngine`, `periodCloseEngine`, VAT logic are Phase-4/M2 units — their fixtures belong to those WPs.
- **No message-text pinning.** `formatPostingValidityError` produces user-facing strings (UX-only, i18n-bound); its output is not a domain rule and is out of scope.
- **No period *generation* logic.** `generateAccountingPeriods` (write path) is not a posting rule; only the read-side `getEffectivePeriodState` is pinned here.
- **No float→integer conversion decisions.** Posting-validity and FX-metadata outputs are amount-agnostic (master-data + date logic), so no money arithmetic is captured; the float→minor-unit translation is a porting concern for the money-bearing WPs, already governed by P1-WP03's `AwayFromZero` decision.

## Source material (salvage vs rewrite)
**Salvage — extract fixtures (playbook §5 step 1).** All units are pure functions taking plain data and returning plain data / throwing typed errors — ideal for golden capture. Exact old-repo references (pin the commit SHA in `SOURCE.md` at capture time):
- `src/shared/postingValidity.ts` — `assertPostingAccountsValid` (L133–161), `evaluateAccountReference` (L95–112), `assertPostingBusinessPartnersValid` (L162–190), `assertPostingUsersValid` (L191–219), `evaluateTimeboundReference` (L113–132), `assertPostingProjectsValid` (L224–261), `assertPostingCurrencyPolicyValid` (L316–344), `resolveEffectiveCurrencyPolicy` (L296–307), `assertPostingPeriodOpen` (L370–388); error classes `PostingValidityError` (L66–74), `CurrencyPolicyError` (~L285), `PeriodClosedError` (~L350).
- `src/shared/periodUtils.ts` — `getEffectivePeriodState`, `EffectivePeriodState`.
- `src/shared/fxPolicy.ts` — `resolveGroupFxPolicy`, `resolveAccountFxPolicy` (L162–188), `buildTransactionLineFxMetadata` (L189–214); `MONETARY_GROUPS`/`HISTORICAL_GROUPS` (L20–44).
- **Seed cases already in the old suite** (run these against the old impl, then expand to full branch coverage): `tests/postingValidity.test.ts`, `tests/accountCurrencyPolicy.test.ts`.

## Accounting / domain sign-off
- **LL Accounting Expert consult: not required for capture.** This WP pins *existing* behavior verbatim; it makes no accounting judgment. Two rule-interpretation questions are **surfaced now but routed later** (to be resolved when P2-WP04/WP05 are planned, not here): (a) should the new system keep "no-period-defined ⇒ posting allowed", or require a period? (b) is currency policy "P&L any / balance-sheet fixed-to-account-currency" the intended target rule? Recorded as open questions in the Phase-2 notes; the fixtures capture the OLD answer either way.
- **Golden fixtures: this WP *is* the fixtures.** N/A as a prerequisite.

## Fixture artifact layout (target)
```
tests/fixtures/golden/ledger-core/
  SOURCE.md                      # old-repo commit SHA + per-unit file/line provenance
  fixture-format.md              # the input/expected JSON schema (language-neutral)
  manifest.json                  # every case: { id, unit, description, file }
  posting-accounts/*.json
  posting-business-partners/*.json
  posting-users/*.json
  posting-projects/*.json
  currency-policy/*.json
  period-state/*.json            # getEffectivePeriodState + assertPostingPeriodOpen
  fx-metadata/*.json
tools/fixtures/                  # (optional) capture harness/script + README to re-capture
```
Each case file: `{ "input": { … }, "expected": { "ok": true } | { "error": { "type": "PostingValidityError" | "CurrencyPolicyError" | "PeriodClosedError", … } } }`. Dates serialized as ISO `yyyy-MM-dd` (no time-of-day, no timezone) to avoid drift between the TS capture and the C# consumer.

## File list (implementation targets — LL Fixture Smith)
- `tests/fixtures/golden/ledger-core/**` — **new**, the fixture set (artifacts + manifest + SOURCE.md + fixture-format.md).
- `tools/fixtures/**` — **new (optional but preferred)**, the capture harness/script + README documenting how to reproduce the artifacts from the old repo.
- `docs/rebuild/status.md` — P2-WP01 row → `done` on completion; session-log entry.
- No changes to `app/**` or `backend/**`.

## Acceptance criteria (verifiable checks — all must pass)
1. **Artifacts exist and are indexed.** `tests/fixtures/golden/ledger-core/` contains per-unit case files and a `manifest.json` listing every case with a stable `id`, `unit`, and `file`; every manifest entry resolves to an existing file and vice-versa (1:1).
2. **Branch coverage.** The manifest covers, at minimum, every documented branch:
   - accounts: `missing`, `inactive` (both purposes), `future` (business), `expired` (business), personal-ignores-window (passes), valid-in-window;
   - business-partner & user: `missing`, `inactive`, `future` (business), `expired` (business), personal-vs-business divergence, valid;
   - project: `missing`, `future`, `expired`, valid-in-window, no-window-passes;
   - currency policy: P&L-any-passes, balance-sheet-match-passes, balance-sheet-mismatch-fails, case-insensitive-passes, empty/missing-skips, multi-issue-collection;
   - period: `open`, `closed`, `locked`, `no-period-defined` (allowed) for both `getEffectivePeriodState` and `assertPostingPeriodOpen`;
   - fx-metadata: monetary group, historical group, unknown/default group, account-override-wins, personal-vs-business, `fxRateDate` midnight-normalized, and each of the six returned fields present.
3. **Error shapes pinned.** For at least one representative failing case per erroring unit, `expected.error` records the exact `type` and the structured payload (`issues[]` with `reason`/`entityType`/`entityId` or `accountCurrency`/`txCurrency`; or `periodName`/`state`/`txDate`) — proving error shape, not just pass/fail.
4. **Provenance recorded.** `SOURCE.md` pins the old-repo commit SHA and the per-unit file/line refs used; artifacts are captured by executing the OLD implementation (harness/script committed or the capture steps documented), not hand-written.
5. **Reproducible & deterministic.** Re-running the capture harness against the pinned SHA reproduces byte-identical artifacts; all dates are ISO `yyyy-MM-dd`; no wall-clock or timezone-dependent values leak into artifacts.
6. **Language-neutral format documented.** `fixture-format.md` specifies the `input`/`expected` schema; a reader can load a case in C# and in TS without bespoke per-unit parsing.
7. **Docs-only-to-tests, no scope bleed.** `git diff --name-only` touches only `tests/fixtures/**`, optionally `tools/fixtures/**`, and `docs/rebuild/status.md`. No `app/**`, no `backend/**`, no CI/workflow edits; repo build/test gates are unaffected (fixtures are not yet wired into a runner).

## Manual / human steps
- The Fixture Smith needs read access to the old repo `Lokkeccs/Accounting` to run the seed tests and capture outputs; pin the commit SHA in `SOURCE.md`.
- No LL Accounting Expert step for capture (see sign-off above).

## Risks & notes
- **Capturing UI float logic by mistake.** The balance/tolerance math lives in `JournalEntryPage.tsx` (float, `<0.01`), not in the salvage units. Guard: fixtures cover only the pure `shared/` functions above; balance is out of scope (P2-WP08).
- **Timezone drift.** The old code uses `Date` with `setHours(0,0,0,0)`/`23,59,59,999`. Capture must serialize to ISO date only and the harness must run in a fixed timezone (UTC) so `future/expired` boundaries are stable.
- **Over-capture.** Do not pin `formatPostingValidityError` strings or period-generation — they are UX / write-path, not posting rules.
- **Fidelity for the port (not this WP):** the consumers (P2-WP03/04) must reproduce these outcomes exactly; any deliberate divergence (e.g. requiring a defined period) must be an ADR, per playbook §5 step 2 — flagged as an open question above, not decided here.

## Implementation notes
- **Captured 2026-07-06** by LL Fixture Smith. Old-repo pin: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`, run under `TZ=UTC`.
- **59 fixtures** under `tests/fixtures/golden/ledger-core/`: posting-accounts (10), posting-business-partners (7), posting-users (7), posting-projects (5), currency-policy (11), period-state (8: 4× `getEffectivePeriodState` + 4× `assertPostingPeriodOpen`), fx-metadata (11: 7× `buildTransactionLineFxMetadata`, 2× `resolveAccountFxPolicy`, 2× `resolveGroupFxPolicy`). Indexed by `manifest.json` (1:1, 0 orphans/dups).
- **Capture method (old repo stays pristine):** committed harness `tools/fixtures/ledger-core/capture.test.ts` (self-contained case defs + old-impl invocation + serializer). Executed by copying into the old repo's `tests/` (matches its `include` glob, `environment: 'node'`), running `npx vitest run`, then deleting the temp copy. `git status` on the old repo confirmed no leftover file. Every `expected` is captured, never hand-authored. Procedure documented in `tools/fixtures/README.md`.
- **Fixture format:** `{ input, expected }` where `expected` is one of `{ ok: true }` / `{ error: { type, … } }` / `{ value: … }`. The `value` variant was added (beyond the plan's `ok|error` wording) for the pure query units (`getEffectivePeriodState`, `resolve*FxPolicy`, `buildTransactionLineFxMetadata`), which return data rather than throwing — required by AC2. Documented in `fixture-format.md`.
- **Error shapes pinned:** `PostingValidityError.issues[]` (`entityType`/`entityId`/`reason`/`txDate`/`source`), `CurrencyPolicyError.issues[]` (`accountId`/`accountCurrency`/`txCurrency`/`reason`/`source`), `PeriodClosedError` (`periodName`/`state`/`txDate`). Multi-issue collection captured for accounts + currency policy.
- **Asymmetries pinned** (port must reproduce): accounts reject `inactive` in both purposes; business partners/users reject `inactive` only in personal (a business `isActive:false` ref **passes** — `bp-/user-inactive-business-allowed`); `future`/`expired` windows are business-only; `no-period-defined` ⇒ allowed.
- **Determinism proven:** SHA-256 hash of the whole fixture tree is byte-identical across two independent captures. All dates ISO `yyyy-MM-dd`; 2-space indent + trailing newline; stable key order; no wall-clock in artifacts (SHA read from git).
- **Deviations from plan:** (a) `value` expected-variant added (justified above); (b) harness lives as a `.test.ts` and is run via copy-into-old-repo rather than an external runner, because the old repo has no `tsx` and its vitest `include` is `tests/**` — this keeps execution semantics identical to the old suite. No app/backend code touched.
- **Not wired into a runner** (deferred to P2-WP03/04/05, which will load this set) — repo build/test gates are unaffected.

## QA verdict
**PASS** — LL QA Reviewer, 2026-07-06. All 7 acceptance criteria independently reproduced; state → `done`.

- **AC1 (artifacts + index):** 59 case files under `tests/fixtures/golden/ledger-core/`; `manifest.json` 1:1 with disk (59 entries = 59 files, 0 orphans, 0 duplicate ids), `count` field = 59.
- **AC2 (branch coverage):** posting-accounts 10 (missing, inactive×2 purposes, future/expired business, personal-ignores-window, valid, both validFrom/validTo boundaries, multi-issue), business-partners 7 & users 7 (missing, inactive personal vs business-allowed divergence, future/expired business, personal-ignores-window, valid), projects 5 (missing/future/expired/valid/no-window), currency-policy 11 (income+expense any, asset match/mismatch, liability+equity mismatch, case-insensitive, missing/empty-tx/empty-account skips, multi-issue), period-state 8 (`getEffectivePeriodState` ×4 + `assertPostingPeriodOpen` ×4 over open/closed/locked/no-period), fx-metadata 11 (monetary/historical/default-asset/default-liability groups, account-override-wins, personal, fxRateDate-normalized, resolveGroup ×2, resolveAccount ×2).
- **AC3 (error shapes):** 23 error cases; verified `PostingValidityError.issues[]` (entityType/entityId/reason/txDate/source), `CurrencyPolicyError.issues[]` (accountId/accountCurrency/txCurrency/reason/source), `PeriodClosedError` (periodName/state/txDate), incl. multi-issue collection for accounts + currency.
- **AC4 (provenance/captured):** `SOURCE.md` pins old-repo SHA `085bedba467e3d46d3889db3bc80ea023e69756e` + per-unit line refs; harness `tools/fixtures/ledger-core/capture.test.ts` executes the old functions; re-run left the old repo pristine (no leftover temp file).
- **AC5 (deterministic):** independent re-capture produced a byte-identical tree (SHA-256 tree hash match); structural scan of all 59 files found 0 time-of-day/timezone leaks; all dates ISO `yyyy-MM-dd`.
- **AC6 (format documented):** `fixture-format.md` specifies `{ input, expected }` with `expected` ∈ `{ok}|{error}|{value}`, per-unit input shapes, and the three error shapes.
- **AC7 (scope):** working-tree diff limited to `tests/fixtures/**`, `tools/fixtures/**`, `docs/rebuild/status.md`, and this plan file; no `app/**`/`backend/**`; CI gates run only in `app/`+`backend/` so fixtures/harness are unaffected.
- **Fidelity spot-check (independent reasoning vs old source):** end-of-day/start-of-day validTo/validFrom boundaries → allowed; liability-in-unknown-group → monetary/revalue; `resolveGroupFxPolicy` group-policy override and `resolveAccountFxPolicy` account override precedence; case-insensitive currency — all captured values match old-code semantics.
- **Adversarial scans:** *hallucination* — the `{value}` expected-variant beyond the plan's `ok|error` wording is required by AC2 (pure query units return data, not throw), documented in `fixture-format.md` and the implementation notes → accepted, not invented. *Patch-layering* — `serializeError` re-throws unexpected error types (`throw e`), so no root-cause is masked; try/catch is scoped to the expected typed guards. *Security/financial* — synthetic data, no secrets (gitleaks repo-wide unaffected); no money amounts in scope (balance invariant correctly deferred to P2-WP08).
- **Non-blocking note (N1):** the two rule-interpretation questions (period-required?, currency-policy target) remain open by design and are routed to LL Accounting Expert at P2-WP04 planning; fixtures pin the OLD answer either way. No action for this WP.

Cleared for LL Git (fixtures + harness + docs).
