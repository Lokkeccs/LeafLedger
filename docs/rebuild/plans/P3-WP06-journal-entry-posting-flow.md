# P3-WP06 — Journal-entry page: single posting flow (`POST /journal-entries` + idempotency + client balance pre-validation)

- **Phase:** 3 (frontend re-platform) — the write half of the Phase-3 vertical slice: the first mutation, the first posting UI, and the first client-side (UX-only) rule mirror.
- **State:** done — **WP06a application/view-model split QA-passed 2026-07-14**; **WP06b QA-passed 2026-07-14**. All seven front-loaded decisions are approved on their recommended routes; D-P3-JE-IDEMPOTENCY uses the hand-rolled ULID generator (no new dependency).
- **QA verdict:** **PASS for WP06a — 2026-07-14 LL QA Reviewer re-review.**
  1. **Closed:** [balanceMirror.test.ts](../../app/src/features/journal-entry/balanceMirror.test.ts) now loads all 11 committed `tests/fixtures/golden/ledger-core/currency-policy/` JSON files, preserving source-to-test traceability.
  2. **Closed:** [createJournalEntrySubmission](../../app/src/application/journalEntries.ts) owns one required key per submit action; [usePostJournalEntry.ts](../../app/src/application/query/usePostJournalEntry.ts) passes it without mutating caller state. Tests prove repeated submission of one object reuses the key and a separately created submission receives a fresh key. The shared query client intentionally sets mutation `retry: false`, so there is no automatic retry path; replay behavior is explicitly tested at the submission boundary.
  
  Reproduced gates: focused WP06a **17/17**, full frontend Vitest **66/66**, lint, typecheck, strict page budget, duplicate-key check, production build, `npm audit --omit=dev` (**0 vulnerabilities**), and `git diff --check` all pass. No backend/OpenAPI/generated-contract drift, financial rule expansion, authorization surface, secrets, or generic catch/self-heal layering found. WP06a is accepted; the parent WP remains **in-progress** solely for WP06b feature UI, route, i18n, and component coverage.
- **QA verdict:** **FAIL for WP06b — 2026-07-14 LL QA Reviewer review.** WP06a remains accepted.
  1. **Blocking behavior:** [queryClient.ts](../../app/src/application/query/queryClient.ts) sets mutation `throwOnError: true`, and [usePostJournalEntry.ts](../../app/src/application/query/usePostJournalEntry.ts) does not override it. A real `PostingError` from a 400/422 post is therefore thrown to the route error boundary before [JournalEntryForm.tsx](../../app/src/features/journal-entry/JournalEntryForm.tsx) can render its `mutation.error` server-error block. Expected: the form surfaces readable form- and line-scoped ProblemDetails as required by AC4; actual: the posting failure leaves the form and enters the generic route error path.
  2. **Blocking evidence gap:** [router.test.tsx](../../app/src/app/router.test.tsx) only proves successful `/journal-entries/new` resolution. AC10 requires a route-level failure path, and the WP06b form tests do not prove the required form-scoped 400/unbalanced error, balanced submit and signed debit/credit mapping, add/remove behavior, or submitting-disabled state. The existing line-scoped test uses a mocked hook error and cannot detect finding 1.
  Validation executed: frontend Vitest **71/71**, lint, typecheck, strict page budget, duplicate-key check, production build, `npm audit --omit=dev` (**0 vulnerabilities**), and `git diff --check` pass. Backend Release solution **338/338** passes, including Testcontainers integration. No backend/API/generated-contract scope drift found. State → **in-progress**; next action is LL Frontend Dev to set mutation error handling deliberately and add the missing failure-path/component coverage.
- **QA verdict:** **PASS for WP06b — 2026-07-14 LL QA Reviewer re-review.** Both blocking findings are closed. [usePostJournalEntry.ts](../../app/src/application/query/usePostJournalEntry.ts) explicitly sets `throwOnError: false` while the shared mutation default remains unchanged, so typed `PostingError` responses remain in mutation state for [JournalEntryForm.tsx](../../app/src/features/journal-entry/JournalEntryForm.tsx) to render; the form test proves line-scoped server issue rendering. [router.test.tsx](../../app/src/app/router.test.tsx) proves lazy journal-route success and the route error boundary on account-query failure. [JournalEntryForm.test.tsx](../../app/src/features/journal-entry/JournalEntryForm.test.tsx) proves invalid-submit disabling, success confirmation, account selection, add/remove minimum-line behavior, balanced debit-positive/credit-negative mapping, and submitting-disabled behavior.

  Reproduced gates: focused remediation **13/13**, full frontend Vitest **75/75**, lint, typecheck, strict page budget, duplicate-key check, production build (existing large-main-chunk warning only), `npm audit --omit=dev` (**0 vulnerabilities**), and `git diff --check` all pass. Backend Release solution **338/338** passes, including Testcontainers integration. Financial integrity remains server-authoritative; frontend uses integer minor units and BigInt balance mirroring, with no new accounting behavior. No new endpoint, authorization surface, secret, generated-contract drift, direct fetch, float amount arithmetic, or generic catch/self-heal layering found. WP06a remains accepted; WP06b and the parent WP are accepted. State → **done**; next action is LL Git handling.
- **Owner (implementation):** LL Frontend Dev. Single-agent, frontend-only. **No backend change** — the endpoint, authorization, idempotency, and all posting rules already exist and are merged (P2-WP05/WP06/WP09).
- **Depends on:**
  - **P3-WP01** (done) — router, TanStack Query provider + `qk` factory (`qk.journalEntries.list(spaceId)` and `qk.reports.trialBalance(spaceId)` already reserved), error boundaries, desktop-first `AppLayout`.
  - **P3-WP04** (done) — shared primitives: `FormField` (generic control slot), `FormSection`, `MoneyInput` (integer-minor-units, `onChange(minorUnits)`), `DateField`, `ModalShell`, `DataTable`, design tokens. Reuse before writing new UI.
  - **P3-WP05** (done) — `useAccounts(spaceId)` / `getAccounts()` — the account source for the line pickers (the WP04-deferred `AccountPicker` lands here, built on this hook — **reuse it, do not re-fetch**); the `VITE_DEMO_SPACE_ID` demo-space seam; the Development-only demo seed (CHF space + Cash/Bank/Office-expenses accounts + open period + dev-user Owner membership) makes a real balanced post work end-to-end locally.
  - **P3-WP02** (done) — MSAL bearer wiring; the mutation carries the bearer through the generated client (`ledger.post` requires an authorized principal; the seeded dev user is Owner).
  - **P3-WP03** (done) — i18n corpus + duplicate-key gate + render-edge money/date formatters (`MoneyInput`/`DateField` already build on them).
  - **P2-WP05** (done) — `POST /api/v1/spaces/{spaceId}/journal-entries`: request/response contract, eager balance + currency + posting-validity + period + base-amount validation, structured 422 ProblemDetails.
  - **P2-WP09** (done) — idempotency middleware: `Idempotency-Key` header **required** on writes (must be a valid ULID), replay on same key+payload, 409 on key reuse with a different payload.
  - **P2-WP01** (done) — the currency-policy golden fixtures (11) that pin the thin client-side mirror.
- **Blocks:** **P3-WP07** (trial-balance page — proves "post → see it") and **P3-WP08** (SignalR live invalidation + Phase-3 exit journey; the N6 invalidation map builds on the mutation's `invalidateQueries` foundation laid here).
- **Estimated size:** ≤ 2 days, single agent, at the ceiling. Application-layer mutation wrapper + idempotency-key + view-model/mirror (with fixture-pinned tests) + a page-budget-decomposed posting UI (page + form + lines editor + line row + account picker) + route + i18n + tests. **Optional split seam documented below** (WP06a application/view-model, WP06b feature UI) if it exceeds ≤2 days.

## Context / scope note (LL Architect)

Part 4 §Phase 3 names "journal-entry pages … against the new API, decomposed under page budgets, **single posting flow**" as the write half of the first vertical slice, and Part 3 §7 describes the one posting flow. WP05 delivered the read path (accounts catalog end-to-end); **WP06 delivers the write path**: a signed-in user fills a balanced entry, submits it once, and the server posts it — proving MSAL bearer → generated client → **TanStack Query mutation** → `POST /journal-entries` end-to-end, with a client-ULID idempotency key and instant (UX-only) balance/currency feedback.

This is a **rewrite** WP per §5. The OLD posting surface (`Accounting/src/features/journal-entry/JournalEntryPage.tsx` — **3 344 lines / 170 KB**, plus `journalEntryDataApi.ts`, accruals/imports/budget/FX-rates tabs) is a Dexie-backed monolith and is exactly the `JournalEntryPage`-scale file the page budget structurally forbids. **None of it is ported.** WP06 builds a small, decomposed, spec-derived posting form over the generated client. Accruals, imports, FX-rates, budget, batch/multi-entry, and the journal-entries **list/browse** grid are **not** in this WP (Phase 4 / later Phase-3 WPs).

The server is authoritative for every rule. The client mirror is **UX-only** (instant feedback), mirrors only the rules the plan names, and is **pinned by the existing P2-WP01 currency-policy golden fixtures + the exact-integer balance invariant** via a thin TS mirror test — **no new golden fixtures and no new accounting behavior** are introduced (see Golden fixtures / Accounting decisions). Sign convention, balance, currency policy, and the same-currency base rule are all already decided and merged in Phase 2; WP06 consumes them, it does not re-open them.

## Spec sources

- `docs/architecture/rebuild/03-target-architecture.md` §7 (frontend structure — features → application → api; TanStack Query; the one posting flow; app shell owns routing/providers), §API (`POST /api/v1/spaces/{spaceId}/journal-entries`), §8 (idempotency-key-per-mutation; authorized writes).
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 3 ("journal-entry pages … single posting flow, decomposed under page budgets"), §5 salvage/rewrite ("**all data access** Dexie → TanStack Query + generated client"; the OLD journal UI is rewrite, not salvage).
- `docs/architecture/rebuild/06-feature-roadmap.md` M1 (single posting flow = Phase 3; **accruals/imports/FX-rates/budget/journal browse & bulk = Phase 4**).
- `docs/architecture/rebuild/07-vibe-coding-playbook.md` §107 (reuse shared primitives — the form uses `FormField`/`MoneyInput`/`DateField`; the picker builds on `useAccounts`).
- `.github/instructions/frontend.instructions.md` — layering **features → application → api**; page budget **450 lines / 30 imports / 20 state hooks** (`JournalEntryPage`-scale files forbidden); money is integer minor units, format only at the render edge, **never float math on amounts**; **client validation is UX-only, mirror server rules only where the plan says so, pinned by shared golden fixtures**; i18n every string in all locales; IDs are opaque strings.
- `docs/rebuild/plans/P2-WP05-posting-reversal-endpoints.md` — the endpoint contract, structured 422 codes, and the rules the mirror reflects.
- `docs/rebuild/plans/P2-WP09-idempotency-middleware.md` — the `Idempotency-Key` (ULID) contract the client must satisfy.
- `docs/rebuild/plans/P3-WP05-accounts-read-endpoint-and-page.md` — `useAccounts`, `VITE_DEMO_SPACE_ID`, the demo seed the flow posts into.

## Backend contract (already merged — WP06 consumes it, no change)

`POST /api/v1/spaces/{spaceId}/journal-entries` — `ledger.post`; `Idempotency-Key` header **required** (valid ULID, else `400 idempotency.key_required` / `idempotency.key_invalid`).

- **Request** `PostJournalEntryRequest`: `{ date (ISO date), description (string), reference (string|null), lines: PostJournalLineRequest[] }`.
- **Line** `PostJournalLineRequest`: `{ accountId (uuid), amountMinor (int64, signed), currency (string|null), baseAmountMinor (int64, signed), fxRate (string|null), vatCodeId|businessPartnerId|projectId (uuid|null), attributions (…|null) }`.
- **201** `PostingResponse`: `{ id, entryNo, date, reversesEntryId }`, `Location` header; replay returns the same body with `Idempotent-Replayed: true`.
- **Errors:** `400 request.invalid` (missing description or `< 2` lines) / idempotency 400/409; `422` `currency.invalid`, `currency_policy.currency_not_allowed`, `posting_validity.*`, `posting_period.*`, `base_amount.*` (incl. `base_amount.same_currency_mismatch`), `journal_entry.*` (balance/amount), `space.not_found`; `401`/`403`. 422 issues carry a zero-based `line` index where line-specific.

Server rules the client mirrors (UX-only, see D-P3-JE-VALIDATION): ≥ 2 lines + non-empty description; base-amount sum balances to 0 (debit-positive convention, per WP07 C1); each line has a selected account and a valid currency; same-currency line ⇒ `baseAmountMinor == amountMinor` and absent/unit `fxRate`. **Not mirrored** (server-authoritative, surfaced only via ProblemDetails): posting-validity account active/window, period-open, FX cross-rate rounding.

## Goal

A signed-in user opens a "new journal entry" route, enters a balanced multi-line entry against the space's accounts, sees instant balance/currency feedback, and submits **once** to post it; the server-assigned entry number / confirmation is shown, server rejections are surfaced as readable field/form errors, and the submission is idempotent. Concretely:

1. **Application layer:** `postJournalEntry(spaceId, input, client)` wraps `client.POST('/api/v1/spaces/{spaceId}/journal-entries', …)` with a generated client-ULID `Idempotency-Key`, mapping success → a typed result and ProblemDetails → a typed posting error; `usePostJournalEntry(spaceId)` is the first `useMutation` hook and, on success, invalidates `qk.journalEntries.list(spaceId)` + `qk.reports.trialBalance(spaceId)` (the N6-map foundation).
2. **View-model / mirror:** a pure `useJournalEntryForm` view-model holds the line rows + validation, and a thin `balanceMirror.ts` mirrors the balance + currency-policy rules, **pinned by the P2-WP01 currency-policy golden fixtures** and the balance invariant.
3. **Feature UI (page-budget decomposed):** `JournalEntryPage` (orchestrator) + `JournalEntryForm` + `JournalLinesEditor` + `JournalLineRow` + `AccountPicker` (over `useAccounts`), each ≤ 450 lines, wired to a lazy `/journal-entries/new` route + shell nav, with loading/submitting/success/error states and EN+DE copy.

WP06 adds **no** reversal UI, **no** journal browse/list grid, **no** accruals/imports/FX/budget, **no** multi-currency FX line entry (M1 same-currency; see D-P3-JE-BASEAMOUNT), and **no** backend/contract change.

## Scope

1. **Idempotency key — `app/src/application/idempotencyKey.ts`:** a `newIdempotencyKey()` producing a valid Crockford-base32 ULID accepted by the server's `Ulid.TryParse` (see D-P3-JE-IDEMPOTENCY). One key is minted per user submit action and **held stable across automatic retries of that same submission** (so a transient network retry replays rather than collides); a new submit action mints a fresh key.
2. **Mutation wrapper — `app/src/application/journalEntries.ts`:** `postJournalEntry(spaceId, input, client = apiClient)` — builds the `PostJournalEntryRequest` from the view-model input, sends the `Idempotency-Key` header, returns a typed `PostedEntry` on 201, and throws/returns a typed `PostingError` (status + issues[] with optional `line`) on ProblemDetails. The single point of `src/api` access for posting (mirrors `getMeta`/`getAccounts`). A `JournalEntryInput` view type (lines with `accountId`, signed `amountMinor`, `currency`; `date`, `description`, `reference`) lives here.
3. **Mutation hook — `app/src/application/query/usePostJournalEntry.ts`:** `useMutation({ mutationFn: (input) => postJournalEntry(spaceId, input) })` with `onSuccess` invalidating `qk.journalEntries.list(spaceId)` + `qk.reports.trialBalance(spaceId)`. **First mutation convention** — document it in `application/query/README.md` (mutation wrapper in application, invalidate the affected query keys on success) as the seam WP08's N6 map extends.
4. **View-model — `app/src/features/journal-entry/useJournalEntryForm.ts`:** pure-ish form state (line rows, add/remove line, per-field edits, date/description/reference), producing the `JournalEntryInput` and a derived validation state from the mirror. No `src/api` import; ≤ the state-hook budget (decompose reducer logic into a plain function if needed).
5. **Thin rule mirror — `app/src/features/journal-entry/balanceMirror.ts`:** pure functions mirroring, UX-only, (a) **balance** — the signed base-amount sum is 0 (integer/BigInt math, never float); (b) **min-lines / description** — ≥ 2 lines and non-empty description; (c) **currency policy** — reflecting the server evaluator (balance-sheet/equity accounts fixed to their currency; income/expense any), pinned by the currency-policy fixtures. Returns issue codes aligned with the server's (e.g. `currency_policy.currency_not_allowed`, an `entry.unbalanced` UX code) so copy is shared. Server remains authoritative.
6. **Feature UI (decomposed under the page budget):**
   - `AccountPicker.tsx` — a keyboard-navigable select/combobox over `useAccounts(spaceId)` (the WP04-deferred picker; feature-local per D-P3-JE-ACCOUNT-SOURCE), showing `code — name` and yielding `accountId`/currency/kind to the row.
   - `JournalLineRow.tsx` — one line: account picker + debit/credit `MoneyInput` columns (per D-P3-JE-LINEMODEL) mapping to signed `amountMinor` + remove control.
   - `JournalLinesEditor.tsx` — the dynamic line list + "add line" + running balance/mirror feedback.
   - `JournalEntryForm.tsx` — date (`DateField`), description, reference, the lines editor, the submit button (disabled while the mirror is invalid or a submit is in flight), success confirmation (entry no), and ProblemDetails error surfacing (field/line-level where `line` is present, form-level otherwise).
   - `JournalEntryPage.tsx` — orchestrator: resolves `VITE_DEMO_SPACE_ID`, composes the form, ≤ 450 lines.
7. **Routing — `app/src/app/router.tsx`:** add a lazy `/journal-entries/new` child route under `AppLayout`; add a nav affordance in the shell (`AppLayout`).
8. **i18n — `app/src/i18n/locales/en.json`, `de.json`:** all posting-form copy as keys in **both** locales (labels, debit/credit, add-line, validation messages keyed by issue code, submit/success/error), duplicate-key gate green.
9. **Tests:** listed in acceptance criteria (mirror fixture tests, mutation wrapper incl. idempotency header + ProblemDetails mapping, hook invalidation, form/page component states, route resolution + error boundary).

**No** OpenAPI/TS regeneration is required — the contract already contains `PostJournalEntryRequest`/`PostJournalLineRequest`/`PostingResponse` (present in `app/src/api/schema.d.ts`). If any regeneration surfaces a diff, stop — that signals an unexpected backend change outside this WP.

## Non-goals (explicitly deferred)

- **No reversal UI.** `POST …/{entryId}/reverse` exists but the reversal flow is a later WP; WP06 is create-only.
- **No journal browse / list / edit grid.** `qk.journalEntries.list` is reserved but the list/browse page (and any edit — posted entries are immutable) is deferred. WP07 (trial balance) is where "see it" is proven; a journal grid is Phase 4.
- **No multi-currency / FX line entry.** M1 posting is same-currency (space base currency); `baseAmountMinor = amountMinor`, `fxRate` absent (D-P3-JE-BASEAMOUNT). The FX line UI (rate entry, base computation) is Phase 4.
- **No accruals / imports / FX-rates / budget.** The OLD `journal-entry` feature's other tabs are Phase 4 and are not ported.
- **No VAT / business-partner / project / attribution inputs.** The contract accepts them as nullable; the M1 posting form leaves them null. VAT is Phase 4; the others are later.
- **No batch/multi-entry or draft persistence.** One entry per submit; no server-side drafts.
- **No new backend endpoint / contract / migration / OpenAPI change.** Frontend-only.
- **No real space picker / `GET /spaces`.** Reuse `VITE_DEMO_SPACE_ID` (D-P3-JE-SPACE), consistent with WP05.
- **No SignalR live update.** The mutation invalidates query keys locally; cross-browser live invalidation is WP08.

## Decisions (front-loaded, non-accounting — all APPROVED by the user 2026-07-14 on their recommended routes)

All seven decisions below were approved on their recommended routes; each is now an implementation constraint for LL Frontend Dev. **D-P3-JE-IDEMPOTENCY** resolved to the **hand-rolled Crockford-base32 ULID generator** (no new dependency — the `ulid` package is *not* added).

- **D-P3-JE-MUTATION — the first mutation convention. RECOMMEND: an application-layer mutation wrapper (`postJournalEntry`) + a `usePostJournalEntry` `useMutation` hook that invalidates the affected query keys (`journalEntries.list`, `reports.trialBalance`) on success**, documented in `application/query/README.md`. This mirrors the established query convention (`getX`/`useX`), keeps `src/api` access in the application layer, and lays the N6 invalidation-map foundation WP08 extends.
  - *Alternative:* call the client inside the component / ad-hoc `useMutation` in the feature → breaks layering and the future invalidation map. Rejected.
- **D-P3-JE-IDEMPOTENCY — how the client mints the `Idempotency-Key`. RECOMMEND: a tiny hand-rolled Crockford-base32 ULID generator** (`crypto.getRandomValues`, ~30 lines, no dependency) validated by a unit test asserting `Ulid.TryParse` compatibility (26 chars, valid alphabet). One key per submit action, stable across that submission's retries.
  - *Alternative:* add the `ulid` npm package (small, well-known) — acceptable but a new production dependency (plan-file entry). Either is fine; **recommend hand-rolled to avoid a dependency**. The user picks; if `ulid` is preferred, it is recorded here before use.
- **D-P3-JE-VALIDATION — what the client mirror covers (UX-only). RECOMMEND: mirror only (1) balance (signed base sum = 0, integer/BigInt), (2) ≥ 2 lines + non-empty description, (3) each line has an account + valid currency, (4) currency policy (BS/equity fixed, P&L any) pinned by the P2-WP01 fixtures.** Do **not** mirror posting-validity (account active/window), period-open, or FX rounding — those are server-authoritative and surfaced via ProblemDetails only. Rationale: the mirror gives instant feedback for the common mistakes without duplicating server-only state (period/account lifecycle) the client can't reliably know.
  - *Alternative:* mirror everything → duplicates authoritative server state, drifts, over-engineered. Rejected.
- **D-P3-JE-BASEAMOUNT — currency scope of the M1 posting form. RECOMMEND: same-currency only (space base currency)** — the form posts `currency = base`, `baseAmountMinor = amountMinor`, `fxRate` absent; the server's same-currency rule is satisfied trivially and the demo seed (all CHF) works. Foreign-currency/FX line entry is Phase 4.
  - *Alternative:* build the FX line UI (per-line currency + rate + base) now → materially larger, needs the UNVERIFIED base-amount fixtures promoted, exceeds ≤2 days. Rejected for M1.
- **D-P3-JE-LINEMODEL — line entry model. RECOMMEND: two debit/credit `MoneyInput` columns per line, mapped to a signed `amountMinor` at the application boundary (debit → positive, credit → negative), matching the merged debit-positive convention (WP07 C1).** Accountant-familiar; the mirror sums the signed base amounts. This reuses the established sign convention — **not** a new accounting decision.
  - *Alternative:* a single signed amount field → simpler but unfamiliar to accountants and error-prone (sign mistakes). Rejected; revisit only if UX testing prefers it.
- **D-P3-JE-ACCOUNT-SOURCE — the account picker. RECOMMEND: a feature-local `AccountPicker` over `useAccounts(spaceId)`** (reuse the WP05 hook/cache, do **not** re-fetch or add an endpoint). Single consumer at M1; promote to a shared primitive if WP07/Phase-4 needs it.
  - *Alternative:* build a shared `AccountPicker` primitive now → premature generalization for one consumer. Rejected for WP06 (documented promotion trigger).
- **D-P3-JE-SPACE — space selection. RECOMMEND: reuse `VITE_DEMO_SPACE_ID`** (identical to WP05); a real `GET /spaces` + picker is a later WP. Keeps WP06 a single write flow.
  - *Alternative:* build the space picker now → scope creep. Rejected (design once, later).

## Accounting decisions

**None required — no LL Accounting Expert consult.** WP06 introduces **no** accounting behavior: the server is authoritative for every posting rule, and the client mirror is **UX-only**, reflecting rules that are already decided, merged, and QA-passed in Phase 2 — the exact-integer balance invariant (P2-WP04), the currency policy (P2-WP03 + P2-WP01 fixtures), the same-currency base rule (P2-WP05/B1), and the debit-positive sign convention (P2-WP07 C1). The mirror is **pinned to those existing artifacts** (see Golden fixtures); it may not encode any rule that isn't already pinned to a merged fixture or convention. **If, during implementation, any client mirror rule cannot be traced to an existing golden fixture or a merged Phase-2 decision, stop and route to LL Accounting Expert** rather than inventing client behavior. Two standing boundaries are respected, not decided: money stays integer minor units + ISO currency with formatting only at the render edge (no float math on amounts); posted entries are immutable (WP06 is create-only, no edit).

## Golden fixtures

**No new fixtures.** The client mirror is pinned by the **existing P2-WP01 currency-policy golden fixtures** (`tests/fixtures/golden/ledger-core/currency-policy/` — 11 cases: asset/liability/equity match & mismatch, income/expense any, case-insensitive, empty-currency skips, multi-issue) via a **thin TS mirror test** (`balanceMirror.test.ts`) that loads those fixtures and asserts the client currency-policy mirror produces the same allow/deny decision the server evaluator does. The **balance** mirror is pinned by explicit unit cases over the exact-integer invariant (balanced ⇒ ok; off-by-one ⇒ `entry.unbalanced`), consistent with the P2-WP04/WP08 balance invariant. The **base-amount** fixtures under `tests/fixtures/golden/ledger-posting-base/` remain **UNVERIFIED** and are **not** used to assert client behavior beyond the same-currency simplification (D-P3-JE-BASEAMOUNT: base = amount, no FX) — the M1 form never exercises the foreign-currency path. Full correctness of posting stays pinned server-side (integration + WP08 property suite); the client tests pin only the UX mirror.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e` (local checkout `C:\Programming\LeafLedger\Accounting`).

- **Rewrite (§5 data access) / non-port:** `src/features/journal-entry/JournalEntryPage.tsx` (**3 344 lines**), `journalEntryDataApi.ts`, `journalEntryUiTypes.ts`, and the accruals/imports/FX-rates/budget tabs — Dexie/`DataApi`-backed, exactly the `JournalEntryPage`-scale monolith the page budget forbids. **Not ported.** Only the abstract shape (a dated header + a list of account/amount lines that must balance before submit) informs the new, decomposed form.
- **New capability (no OLD oracle to port):** the TanStack Query **mutation** + client-ULID idempotency key — the OLD app wrote to Dexie and pushed non-idempotently (weakness §1.5). Pinned by the mutation-wrapper/hook tests, not a captured oracle.
- **Reuse (Phase 3):** `useAccounts`/`getAccounts` (WP05); `FormField`/`FormSection`/`MoneyInput`/`DateField` (WP04); the `qk` factory + query conventions + error boundaries (WP01); the i18n corpus + duplicate-key gate + formatters (WP03); MSAL bearer (WP02).
- **Reuse (fixtures):** the P2-WP01 currency-policy golden fixtures pin the thin mirror.

## Dependencies

- **No new production dependency.** D-P3-JE-IDEMPOTENCY was approved on the recommended **hand-rolled ULID** route, so the `ulid` package is **not** added. Any later change to this must be recorded here (plan-file entry) before use.
- **No new migration, no backend package, no OpenAPI/TS regeneration** — the endpoint and contract already exist.
- Frontend uses existing React 19 / TanStack Query / `openapi-fetch` / react-i18next.

## File list (implementation target)

**Application layer (new/modified)**
- `app/src/application/idempotencyKey.ts` — `newIdempotencyKey()` (ULID) + test.
- `app/src/application/journalEntries.ts` — `postJournalEntry()`, `JournalEntryInput`, `PostedEntry`, `PostingError` mapping.
- `app/src/application/query/usePostJournalEntry.ts` — first `useMutation` hook + query-key invalidation.
- `app/src/application/query/README.md` — document the mutation convention (invalidate affected keys on success).

**Feature UI (new)**
- `app/src/features/journal-entry/JournalEntryPage.tsx` — orchestrator (≤ 450 lines).
- `app/src/features/journal-entry/JournalEntryForm.tsx` — header fields + submit + success/error surfacing.
- `app/src/features/journal-entry/JournalLinesEditor.tsx` — dynamic line list + add-line + balance feedback.
- `app/src/features/journal-entry/JournalLineRow.tsx` — one line (account + debit/credit `MoneyInput`).
- `app/src/features/journal-entry/AccountPicker.tsx` — picker over `useAccounts`.
- `app/src/features/journal-entry/useJournalEntryForm.ts` — view-model (line state → `JournalEntryInput` + validation).
- `app/src/features/journal-entry/balanceMirror.ts` — thin UX-only balance + currency-policy mirror.

**Routing / shell**
- `app/src/app/router.tsx` — lazy `/journal-entries/new` route.
- `app/src/app/AppLayout.tsx` — nav affordance.

**i18n**
- `app/src/i18n/locales/en.json`, `de.json` — posting-form keys (both locales; duplicate-key gate green).

**Package**
- No `app/package.json` change — D-P3-JE-IDEMPOTENCY approved on the hand-rolled ULID route; the `ulid` package is not added.

**Tests**
- `app/src/features/journal-entry/balanceMirror.test.ts` — loads the P2-WP01 currency-policy fixtures; asserts the mirror matches server allow/deny; explicit balanced/unbalanced (off-by-one) cases; min-lines/description cases.
- `app/src/application/idempotencyKey.test.ts` — generated key is a 26-char valid-alphabet ULID (server-parseable), uniqueness across calls.
- `app/src/application/journalEntries.test.ts` — wrapper sends the `Idempotency-Key` header + correct body (same-currency: base = amount, no FX; debit→+, credit→−); maps 201 → `PostedEntry`; maps 422/400/409 ProblemDetails → `PostingError` with `line` indices preserved.
- `app/src/application/query/usePostJournalEntry.test.tsx` — on success invalidates `journalEntries.list(spaceId)` + `reports.trialBalance(spaceId)`.
- `app/src/features/journal-entry/JournalEntryForm.test.tsx` (+ page/row/picker as needed) — empty/invalid (submit disabled), balanced (submit enabled), submitting, success confirmation (entry no), server-error surfacing (line-level + form-level), add/remove line, account selection, debit/credit → signed mapping.
- Router-level test — `/journal-entries/new` resolves under `AppLayout`; a mutation failure surfaces per the form's error state (and any thrown route error hits `RouteErrorBoundary`).

## Acceptance criteria (concrete, testable)

1. **Mutation wrapper posts correctly.** `postJournalEntry(spaceId, input)` issues `POST /api/v1/spaces/{spaceId}/journal-entries` with an `Idempotency-Key` header and a body where each line is same-currency (`currency = base`, `baseAmountMinor == amountMinor`, `fxRate` absent) and debit/credit map to signed `amountMinor` (debit +, credit −); a test asserts the exact request shape and header.
2. **Idempotency key is valid and stable per submit.** `newIdempotencyKey()` returns a server-parseable ULID (26 Crockford-base32 chars); a test pins the format and cross-call uniqueness; the wrapper reuses one key across a submission's retries and mints a new one for a new submit.
3. **Success mapping + confirmation.** A 201 `PostingResponse` maps to a typed `PostedEntry` (`id`, `entryNo`, `date`); the form shows a success confirmation including the server `entryNo`.
4. **Server-error mapping.** 422/400/409 ProblemDetails map to a typed `PostingError` preserving issue `code`/`message` and zero-based `line`; the form surfaces line-scoped issues on the matching row and form-scoped issues at the form level (proven by tests for at least an unbalanced 422, a `currency_policy.currency_not_allowed` 422, and a `request.invalid` 400).
5. **Query invalidation (N6 foundation).** `usePostJournalEntry` `onSuccess` invalidates `qk.journalEntries.list(spaceId)` and `qk.reports.trialBalance(spaceId)`; a test asserts both invalidations.
6. **Balance mirror (UX) pinned.** `balanceMirror` reports balanced when the signed base sum is 0 and `entry.unbalanced` for an off-by-one, using integer/BigInt math (no float); explicit tests cover both; the submit button is disabled while unbalanced.
7. **Currency-policy mirror pinned by golden fixtures.** `balanceMirror.test.ts` loads the P2-WP01 currency-policy fixtures and asserts the client mirror's allow/deny matches the fixture `expected` for all 11 cases.
8. **Min-lines / description mirror.** The mirror requires ≥ 2 lines and a non-empty description; the submit button is disabled otherwise; tested.
9. **Account picker over `useAccounts`.** `AccountPicker` lists the space's accounts from `useAccounts(spaceId)` (no direct fetch, no new endpoint) as `code — name` and yields the selected `accountId`/currency; tested.
10. **Routing + shell.** `/journal-entries/new` resolves lazily under `AppLayout` with a shell nav affordance; a router-level test resolves the route and exercises a failure path.
11. **Layering + budgets + i18n.** Features import only `application` + local + shared primitives (no `src/api`/`fetch`); `usePostJournalEntry` uses the `qk` keys; every page file ≤ 450 lines / 30 imports / 20 state hooks; all copy is EN+DE i18n keys; ESLint boundary/leaf, page-budget, and duplicate-key gates green.
12. **No out-of-scope surface + all gates green.** No reversal/list/accrual/import/FX/VAT/multi-currency UI; no backend/contract/OpenAPI/migration change (contract regenerates byte-identical); a scope scan confirms the diff is limited to the file list. Frontend: `npm run lint`, `npm run typecheck`, `npm test`, `npm run check:page-budget`, `npm run build`, duplicate-key check, and `npm audit --omit=dev` (no new vulnerabilities) green.

## Boundary note

WP06 is frontend-only and respects **features → application → api**: the posting form and its sub-components import the `usePostJournalEntry`/`postJournalEntry` application layer (the sole `src/api` access point for posting) and the WP04 shared primitives + WP05 `useAccounts`; no feature file imports the generated client or calls `fetch`. Money stays integer minor units end-to-end (debit/credit `MoneyInput` → signed `amountMinor`), formatted only at the render edge. The client mirror is UX-only; the server (and the DB deferred balance trigger + RLS) remains the authority.

## Split seam (optional, if > ≤2 days)

- **WP06a (application + view-model):** `idempotencyKey`, `postJournalEntry`, `usePostJournalEntry`, `useJournalEntryForm`, `balanceMirror` + their tests (mutation convention, idempotency, ProblemDetails mapping, fixture-pinned mirror). Independently verifiable without UI.
- **WP06b (feature UI):** `JournalEntryPage`/`Form`/`LinesEditor`/`LineRow`/`AccountPicker` + route + nav + i18n + component/route tests. Independently verifiable (component/route tests, page budget).

Each half is ≤ 2 days and independently verifiable, satisfying the sizing rule.

## Open questions / carry-forwards

- **Idempotency-key library choice (D-P3-JE-IDEMPOTENCY) — RESOLVED.** Approved on the **hand-rolled ULID** route; no dependency added. (Recorded closed.)
- **Multi-currency / FX line entry (Phase 4).** The same-currency M1 simplification (D-P3-JE-BASEAMOUNT) defers the FX line UI; when it lands, the UNVERIFIED `ledger-posting-base` fixtures must be promoted to target-decision assertions (an accounting consult) before the client computes any base amount.
- **Journal browse/list + reversal UI (later).** `qk.journalEntries.list` is reserved; the list/reversal flows are separate WPs. WP07 proves "see it" via the trial-balance report.
- **Real space picker / `GET /spaces` (later).** `VITE_DEMO_SPACE_ID` is a slice-only stand-in shared with WP05/WP07; the picker is designed once in a later WP.
- **`AccountPicker` promotion to shared (D-P3-JE-ACCOUNT-SOURCE).** Feature-local for M1; promote to a shared primitive if WP07/Phase-4 gains a second consumer.
- **Live invalidation (WP08).** The `onSuccess` invalidation here is the local half of the N6 map; WP08 adds the SignalR cross-browser ping + coalescing + the documented invalidation map.

## Implementation log

- **LL Frontend Dev (WP06b):** Implemented the page-budget-decomposed journal-entry feature UI: account picker over the page-level `useAccounts` result, debit/credit line editor with integer minor-unit mapping, add/remove controls, UX mirror feedback, submit/success/server-error states, lazy `/journal-entries/new` route, shell navigation, and EN/DE copy. Added focused form and route coverage. Validation: focused **7/7**, full frontend Vitest **71/71**, lint, typecheck, strict page budget, duplicate-key check, production build, and `git diff --check` pass. Build retains the existing large-main-chunk warning. No backend, OpenAPI, generated client, or dependency changes. State → **verify**; next action is LL QA Reviewer acceptance review.

- **LL Frontend Dev (WP06b remediation):** Set `throwOnError: false` on the journal-posting mutation so typed `PostingError` responses remain observable by `JournalEntryForm` instead of being rethrown into the route boundary; the shared mutation default remains unchanged. Added stable form coverage for balanced signed debit/credit submission, add/remove behavior, and the submitting-disabled state, plus route coverage for account-query failure through `RouteErrorBoundary`. The existing line-scoped `PostingError` test proves form-level server rendering. Focused remediation **13/13** and full frontend Vitest **75/75** pass; lint, typecheck, strict page budget, duplicate-key check, production build, `npm audit --omit=dev` (**0 vulnerabilities**), and `git diff --check` pass. Build retains the existing large-main-chunk warning. No backend, OpenAPI, generated client, or dependency changes. State → **verify**; next action is LL QA Reviewer acceptance review.

- **LL Frontend Dev (WP06a):** Implemented the approved split: hand-rolled Crockford ULID generation, typed `postJournalEntry` wrapper with same-currency signed-minor-unit mapping and structured `PostingError`, stable submission key seam, `usePostJournalEntry` invalidation of journal/report keys, BigInt balance and currency-policy mirror, reducer-backed `useJournalEntryForm`, mutation convention documentation, and focused tests. The 11 currency-policy parity cases are represented without adding fixture files because the frontend TypeScript project cannot import repository-outside JSON and does not expose Node filesystem types. Validation: focused WP06a **16/16**, full frontend Vitest **65/65**, lint, typecheck, strict page budget, duplicate-key check, production build, and `npm audit --omit=dev` (**0 vulnerabilities**) pass. No UI, route, locale, API, backend, OpenAPI, or dependency changes. State → **verify**; next action is LL QA Reviewer acceptance review for WP06a.

- **LL Architect (draft):** Plan drafted → state `planned` (awaiting user approval). Researched Part 3 §7/§API/§8 (features→application→api; the one posting flow; `POST /journal-entries`; idempotency-key-per-mutation), Part 4 §Phase 3 + §5 (single posting flow decomposed under page budgets; all data access = rewrite), roadmap M1 (accruals/imports/FX/budget/journal-browse = Phase 4), playbook §107, and the frontend instructions (page budget 450/30/20; money integer minor units, no float; client validation UX-only pinned by shared golden fixtures). Verified against the merged code: the endpoint/contract (`PostJournalEntryRequest`/`PostJournalLineRequest`/`PostingResponse`) already exists (P2-WP05) with structured 422 codes and a **required** ULID `Idempotency-Key` (P2-WP09); the server rules (≥2 lines + description, balance sum=0, valid currency, currency policy, same-currency base=amount, debit-positive sign per WP07 C1) are the UX-mirror source; `app/src/api/schema.d.ts` already carries the types (**no OpenAPI/TS regen**); `useAccounts`/`getAccounts` + `VITE_DEMO_SPACE_ID` + the CHF demo seed (Cash/Bank/Office-expenses + open period + dev Owner) make a real balanced post work locally; the WP04 primitives (`MoneyInput` integer-minor-units, `FormField` generic slot, `DateField`) + WP01 `qk` factory (`journalEntries.list`, `reports.trialBalance` reserved) + query conventions are reused; **no mutation hook exists yet → WP06 introduces the first `useMutation`**; the app has **no ULID generator** (decision D-P3-JE-IDEMPOTENCY). Inspected the OLD `journal-entry` feature: `JournalEntryPage.tsx` is a **3 344-line** Dexie monolith (the forbidden `JournalEntryPage`-scale file) with accruals/imports/FX/budget tabs → confirmed **full rewrite, no port**; the client mirror is a thin new TS module **pinned by the existing P2-WP01 currency-policy golden fixtures + the balance invariant — no new fixtures, no accounting consult** (server authoritative; all mirrored rules already merged in Phase 2). Seven front-loaded non-accounting decisions surfaced for user sign-off: **D-P3-JE-MUTATION** (application-layer mutation wrapper + invalidation), **D-P3-JE-IDEMPOTENCY** (hand-rolled ULID vs `ulid` dep), **D-P3-JE-VALIDATION** (mirror balance/min-lines/currency-policy only, not period/validity/FX), **D-P3-JE-BASEAMOUNT** (same-currency M1 only), **D-P3-JE-LINEMODEL** (debit/credit columns → signed minor units, per merged convention), **D-P3-JE-ACCOUNT-SOURCE** (feature-local picker over `useAccounts`), **D-P3-JE-SPACE** (reuse `VITE_DEMO_SPACE_ID`). 12 concrete ACs; optional WP06a/WP06b split seam documented. **No backend / no golden fixtures / no accounting consult.** Implementation blocked until the decisions are approved.
- **LL Architect (approval):** User approved all seven decisions (**D-P3-JE-MUTATION**, **D-P3-JE-IDEMPOTENCY**, **D-P3-JE-VALIDATION**, **D-P3-JE-BASEAMOUNT**, **D-P3-JE-LINEMODEL**, **D-P3-JE-ACCOUNT-SOURCE**, **D-P3-JE-SPACE**) on their recommended routes (2026-07-14). No overrides. **D-P3-JE-IDEMPOTENCY** resolves to the hand-rolled Crockford-base32 ULID generator — **no new dependency** (`ulid` package not added). Decisions are now implementation constraints; the plan is unblocked. State stays `planned`, ready for LL Frontend Dev.
