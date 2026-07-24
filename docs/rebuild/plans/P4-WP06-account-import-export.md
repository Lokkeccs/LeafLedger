# P4-WP06 — Account import/export (CSV, owner-by-email deferred)

- **Phase:** 4 (feature porting) — Stage A (complete M1). The **self-migration on-ramp for the chart of accounts**: bulk export of the account catalog and bulk import back through the P4-WP05 write path.
- **State:** **WP06a `done` — WP06b `done`** after QA re-review on 2026-07-24. No accounting consult and no golden fixtures are required (see those sections).
- **Owner (implementation):** LL Backend Dev (WP06a) → LL Frontend Dev (WP06b). Dual-deliverable; see the split seam.
- **Depends on:**
  - **P4-WP05** (Chart-of-Accounts write endpoints + management UI) — **done; merged via PR #40 on 2026-07-23**. WP06 import applies rows through the exact same `IAccountManagementService` create/update/activate + group-create/update path (idempotent, RLS/actor-bound, audited, domain-validated). It adds **no new validation** — every imported row is validated by the WP05 domain factories and returns the same stable `422` codes. WP05 supplies the service, contracts, `accounts.manage` permission, `GroupCatalogReport`, and the frontend accounts feature required by WP06.
  - **P2-WP03** (done) — the `ChartOfAccounts` domain factories (`Account.Create`, `AccountGroup`, `AccountCodeRange.Create`, `CurrencyCode`) that WP05's write service already invokes; import reuses them transitively, unchanged.
  - **P2-WP02** (done) — the `accounts` + `account_groups` tables, `UNIQUE(space_id, code)`, the `account_groups_no_overlap` GiST exclusion, RLS policies, audit triggers, and `leafledger_app` write grants. **No new migration / grant / schema change.**
  - **P2-WP06** (done) — the `RequireSpacePermission` filter + role→permission matrix (`accounts.manage` for writes, `ledger.read` for reads). Import is `accounts.manage`; export is `ledger.read`.
  - **P2-WP09** (done) — the `idempotency_keys` + advisory-lock + request-hash + replay envelope reused for the bulk import (non-negotiable #4).
  - **P3-WP05 / P3-WP06** (done) — the read pattern + `AccountView`/`GroupCatalogReport`, the `getAccounts`/`useAccounts` + `newIdempotencyKey()` + `ModalShell`/`FormField` frontend precedents the import/export UI reuses.
  - **P4-WP01** (design-system foundation) — **soft dependency** (upload/report surfaces render through token-driven primitives).
- **Blocks:** nothing hard. It is a **required contributor to the M1 launch gate** — the MVP acceptance criterion "a validated self-migration path (old-app export → new-app import round-trip for accounts…)" ([06-feature-roadmap.md](../architecture/rebuild/06-feature-roadmap.md) line 48). The journal + master-data halves of that round-trip are P4-WP16/P4-WP07/P4-WP08.
- **Estimated size:** ≤ 2 days per half across two agents; **execute as WP06a (backend) → WP06b (frontend)** (frontend types come from the regenerated OpenAPI; the combined effort — CSV read/write + bulk-apply service + export endpoints + row-indexed validation report + idempotency + integration + regen, then upload/preview/report/download UI + i18n + tests — exceeds the ceiling as one WP). Each half is independently verifiable.

## Context / scope note (LL Architect)

The [feature roadmap](../architecture/rebuild/06-feature-roadmap.md) M1 "Chart of accounts" line names *account import/export (CSV/XLSX incl. owner-by-email)*, and line 5 makes the **motivation** explicit: adoption is per-space, **no data is migrated**, and existing users start fresh "assisted by **CSV export** from old → import into new, plus an opening-balance path." The MVP acceptance (line 48) requires a **validated self-migration round-trip for accounts**. P4-WP06 delivers that on-ramp for the chart: export the account catalog to a canonical file and import a file back, each row applied through the already-authorized/idempotent/RLS/audited/domain-validated P4-WP05 write path.

This is a **rewrite**, not a port (§5). The OLD import/export is a **client-side SheetJS (`xlsx`) path** in [`src/features/accounts/view-model/useAccountsViewModel.ts`](https://github.com/Lokkeccs/Accounting/blob/085bedba/src/features/accounts/view-model/useAccountsViewModel.ts) — `handleExportAccounts` (L207, `XLSX.writeFile('accounts.xlsx')`), `handleDownloadTemplate` (L342, `accounts_template.xlsx`), and `handleImportFile` (L413, `XLSX.read` → `sheet_to_json` → row-map → Dexie `DataApi` write). "All data access Dexie → TanStack Query + generated client" (§5), and the OLD `xlsx` dependency carries known prototype-pollution CVEs, so the OLD path is **not ported**; only its *column intent* informs the new canonical format, and the *row validation* already lives, fixture-pinned, in the P4-WP05 write service.

**M1 field-set truncation (load-bearing).** The OLD export carries many columns whose backing features do not exist in the M1 schema — `cashFlowActivity`, `isCashEquivalent`, `description`, `iban`, `investmentRole`, `fxTreatment`, `fxRateTimingDefault`, `closingRevalue`, `vatFxMethodOverride` (cash-flow policy / investments / FX overrides / VAT = Phase-4/M2), and `owner`/`ownerEmail`/`owners`/`ownersEmail` (attribution = **P4-WP11**). The current `accounts` row is exactly `code, name, currency, kind, is_active, group_id, valid_from, valid_to, fx_policy` (`AccountView`), and a group is `name, range_start, range_end, parent_id, fx_policy` (`GroupView`). **The M1 canonical import/export column set is therefore the WP05-supported fields only.** Owner-by-email is deferred with the attribution feature (see D-P4-IMP-OWNER); the M2 policy columns are tolerated-but-ignored on import (a per-row warning) and omitted on export until their features land. This keeps WP06 ≤ 2 days and smuggles in no un-consulted accounting behavior.

## Spec sources

- `docs/architecture/rebuild/06-feature-roadmap.md` — M1 "Chart of accounts" (line 36: import/export CSV/XLSX incl. owner-by-email), line 5 (self-migration = **CSV export→import**, no data migration), **line 48** (MVP acceptance = *validated self-migration round-trip for accounts*), and the Imports-module line 42 (**server-side** staging — journal imports, P4-WP16, a separate module).
- `docs/architecture/rebuild/03-target-architecture.md` §"Modules" (Chart of Accounts owns accounts/groups), §API (`CRUD …/accounts|groups`), §8 (AuthZ: role→permission; RLS second wall).
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 4 (dependency-ordered porting), §5 (**all data access → TanStack Query + generated client**; the OLD Dexie/SheetJS path is not ported; fidelity protocol — do not invent behavior).
- `docs/architecture/rebuild/07-vibe-coding-playbook.md` §107 (reuse shared primitives), non-negotiable #4 (every write endpoint idempotent + authorized).
- `.github/instructions/backend.instructions.md` — EF/Npgsql inside Ledger Infrastructure; RLS second wall; authorized/idempotent writes.
- `.github/instructions/frontend.instructions.md` — layering **features → application → api**; page budget (450 lines / 30 imports / 20 state hooks); i18n every string in all locales; IDs are opaque strings; reuse shared primitives; desktop-first; client validation UX-only.
- `docs/rebuild/plans/P4-WP05-chart-of-accounts-write-endpoints.md` — the write service (`IAccountManagementService`), `accounts.manage`, the idempotency envelope, `GroupCatalogReport`, and the accounts feature this WP extends.
- `docs/rebuild/plans/P2-WP09-idempotency-middleware.md` — the idempotency envelope reused for the bulk import.
- OLD reference (behavior oracle only, not ported): `src/features/accounts/view-model/useAccountsViewModel.ts` `handleExportAccounts` (L207), `handleDownloadTemplate` (L342), `handleImportFile` (L413) @ `085bedba`.

## Backend design (WP06a — export + bulk import endpoints)

**No migration.** WP06a adds a CSV serializer/parser, a bulk-apply service that composes the existing WP05 write service, two export endpoints, one import endpoint, and the OpenAPI/TS regen.

- **Canonical CSV shape (M1).** Two document kinds, each a UTF-8 RFC-4180 CSV with a header row:
  - **Accounts:** `kind,code,name,currency,group,isActive,validFrom,validTo,fxPolicy` — where `group` is the **group name** (accounts reference groups by name in the file; resolved to `group_id` on import — see D-P4-IMP-GROUP-REF), `kind` is the `AccountKind` token, `isActive` is `TRUE|FALSE`, dates are ISO `yyyy-MM-dd` or empty, `fxPolicy` is the opaque optional string.
  - **Groups:** `name,rangeStart,rangeEnd,parent,fxPolicy` — where `parent` is the parent group **name** or empty.
  - The header is the versioned contract; export emits exactly these columns and import accepts them (unknown/extra columns → ignored with a per-row warning; the deferred owner/policy columns are the expected "ignored" set — D-P4-IMP-OWNER).
- **Export (`ledger.read`, no idempotency — reads):**
  - `GET …/accounts/export` → `text/csv` attachment (`accounts.csv`) built from the RLS-bound account catalog + a `group_id`→name resolution.
  - `GET …/groups/export` → `text/csv` (`account-groups.csv`) from the RLS-bound group catalog.
  - Deterministic row order (by code / range start) so the round-trip and diffs are stable and testable.
- **Import (`accounts.manage`, idempotent):**
  - `POST …/groups/import` (multipart CSV **or** a JSON `{ rows: [...] }` body — see D-P4-IMP-PARSE) → parse → validate every row through `AccountGroup`/`AccountCodeRange.Create` → **all-or-nothing apply** (D-P4-IMP-TXN) through `CreateAccountGroupAsync`/`UpdateAccountGroupAsync` (upsert-by-name — D-P4-IMP-UPSERT) → `ImportReport`.
  - `POST …/accounts/import` → parse → resolve `group` name → `group_id` (unknown group → row error) → validate through `Account.Create` → all-or-nothing apply through `CreateAccountAsync`/`UpdateAccountAsync` (upsert-by-code) → `ImportReport`.
  - Groups import first, then accounts (the account rows resolve against already-imported groups).
- **`ImportBulkService` (Ledger Infrastructure):** one transaction per import request. Bind RLS + `app.current_actor` once; enforce/replay/store **one** idempotency key for the whole file (request hash = the parsed rows) exactly like posting; loop rows invoking the WP05 create/update methods **within the same bound transaction**; if **any** row fails validation, **roll back** and return `422` with a row-indexed `ImportReport` (no partial writes); if all succeed, commit and return `200` with the per-row `created|updated` outcomes; enqueue one space invalidation. Retried same file+key → the original report replayed (`Idempotent-Replayed: true`); same key + different file → `409 idempotency.key_reused`.
- **Contracts (`Application/Accounts/AccountImportContracts.cs`):** `ImportRowResult(int RowNumber, string Outcome, IReadOnlyList<LedgerProblemError> Errors)`, `ImportReport(int Total, int Created, int Updated, int Failed, IReadOnlyList<ImportRowResult> Rows)`, and (for the JSON path) `AccountImportRow`/`GroupImportRow` request records. Reuse the existing `LedgerProblemError { code, message, field? }` extension shape so row errors match the single-write 422s.
- **CSV read/write (`Infrastructure/AccountCsv.cs`):** a small, well-tested RFC-4180 serializer/parser for the fixed narrow schema (quote fields containing `,`/`"`/newline; double embedded quotes; trim BOM; tolerate `\r\n`/`\n`). See D-P4-IMP-FORMAT for the dependency posture.
- **Routes (extend `LedgerEndpoints`):** `GET …/accounts/export`, `GET …/groups/export` (`ledger.read`); `POST …/accounts/import`, `POST …/groups/import` (`accounts.manage`, `Idempotency-Key` required, `Produces` 200 `ImportReport` / 400 / 401 / 403 / 409 / 422). All 422 row errors use stable ProblemDetails codes.
- **OpenAPI/TS regeneration (REQUIRED):** rebuild `backend/openapi/leafledger-v1.json` + regenerate `app/src/api/schema.d.ts`; CI `contract` gate green. Add the new import operation IDs to the centralized `Idempotency-Key` transformer (the WP05 precedent).
- **No new permission** (reuse `accounts.manage` / `ledger.read`). **No EF entity/model change → `HasPendingModelChanges()` stays green.**

## Frontend design (WP06b — import/export UI)

- **Application layer — `app/src/application/accountImport.ts`** (new): `exportAccountsCsv`/`exportGroupsCsv` (fetch the file via the generated client, return a blob/text for download) and `importAccountsCsv`/`importGroupsCsv` (post the file/rows, map the `ImportReport`, throw a typed `AccountManagementError` on transport failure — the row-level 422 report is a **returned value**, not a throw, so the UI can render per-row results). Import submissions carry a `newIdempotencyKey()`.
- **Query hooks / keys:** `useImportAccounts`/`useImportGroups` mutations; on success invalidate `qk.accounts.list` / `qk.accountGroups.list` (+ the report keys already wired in `usePostJournalEntry`: trial-balance/BS/P&L/dashboard) via the N6 `invalidationMap.ts`. Export is a direct action (no cache), or a `useQuery`-free imperative call.
- **Feature — `app/src/features/accounts/`** (extend): behind `accounts.manage`, an **Import / Export** affordance on `AccountsPage` (and the groups surface): "Export accounts", "Export groups" download buttons; an `ImportModal` (`ModalShell`) with a file picker (`.csv`), a **preview/report table** rendering the returned `ImportReport` (row number, outcome, inline error messages), and a disabled-while-submitting apply. Client does **UX-only** pre-checks (non-empty file, header present); the server is authoritative and its row-indexed 422 renders in the report. ≤ 450 lines per file (decompose).
- **Capability gating:** render affordances; rely on the server `accounts.manage` 403 (friendly toast) — no client rights table (D-P4-COA-UIGATE, unchanged from WP05). i18n all copy (EN + DE).

## Goal

An Owner/Admin can **round-trip their chart of accounts through files**: export accounts and groups to canonical CSV, and import a CSV back so every row is created/updated through the P4-WP05 write path — idempotent, RLS-scoped, audited, domain-validated, all-or-nothing, with a row-indexed result/error report rendered inline. This delivers the accounts half of the M1 self-migration on-ramp. It adds **no** account ownership/attribution import (P4-WP11), **no** journal or master-data import (P4-WP16/WP07/WP08), **no** M2 policy columns, and **no** new accounting rule.

## Scope

### WP06a — backend (LL Backend Dev)
1. `Application/Accounts/AccountImportContracts.cs` — `ImportReport`/`ImportRowResult`, JSON row records, service interface.
2. `Infrastructure/AccountCsv.cs` — RFC-4180 CSV read/write for the fixed accounts + groups schemas.
3. `Infrastructure/AccountImportService.cs` (`ImportBulkService`) — one-transaction, idempotent, RLS/actor-bound, all-or-nothing bulk apply composing the WP05 create/update methods; group-name→id resolution; upsert semantics.
4. `Infrastructure/AccountCatalogService.cs`/`GroupCatalogService.cs` — reuse the existing RLS-bound reads to build the export payloads (or a thin `IAccountExportService`).
5. `Infrastructure/LedgerEndpoints.cs` — two export routes (`ledger.read`) + two import routes (`accounts.manage`, idempotency-key + ProblemDetails).
6. `Infrastructure/LedgerModule.cs` — register the new service(s).
7. `Host/OpenApi/...` transformer — add the import operation IDs to the `Idempotency-Key` set.
8. `backend/openapi/leafledger-v1.json` + `app/src/api/schema.d.ts` — regenerated (contract gate green).
9. Backend integration tests (`postgres:17`) — see acceptance criteria.

### WP06b — frontend (LL Frontend Dev)
10. `app/src/application/accountImport.ts` — export/import wrappers + idempotency + typed errors + `ImportReport` mapping.
11. `app/src/application/query/` — `useImportAccounts`/`useImportGroups` + `invalidationMap.ts` extension (no new `qk` namespace needed; reuse `accounts`/`accountGroups`/report keys).
12. `app/src/features/accounts/` — export buttons + `ImportModal` + report table on `AccountsPage`/groups surface (≤ 450 lines each).
13. `app/src/i18n/locales/en.json`, `de.json` — all import/export copy in both locales (duplicate-key gate green).
14. Frontend tests — see acceptance criteria.

## Non-goals (explicitly deferred)
- **No owner-by-email application — P4-WP11.** Owner/ownerEmail/owners/ownersEmail columns are tolerated-and-ignored on import (per-row warning) and omitted on export until attribution editing lands. Full owner-by-email round-trip returns when P4-WP11 adds the ownership write path (and any attribution accounting consult).
- **No M2 policy columns.** cashFlowActivity, isCashEquivalent, investmentRole, fxTreatment/fxRateTimingDefault/closingRevalue, vatFxMethodOverride, description, iban are not in the M1 schema → ignored on import, omitted on export.
- **No XLSX in M1 (recommended — D-P4-IMP-FORMAT).** CSV is the canonical self-migration interchange format (roadmap line 5). XLSX is an explicit follow-up (a thin add once CSV is proven), not this WP.
- **No journal or master-data import.** Journal import = P4-WP16 (Imports module, server-side staging); master data = P4-WP07/WP08. WP06 is the accounts/groups slice of the round-trip only.
- **No new validation rules.** Import reuses the WP05 domain factories and the C-P4-COA-EDIT mutability/lifecycle policy verbatim; a posted account's immutable field in an import row fails that row with the same `422 account.field_immutable_after_posting`.
- **No hard delete via import.** Import creates/updates only; it never deletes an account/group absent from the file (a file is additive/upsert, not a mirror). Deactivation is expressible via the `isActive` column.
- **No new SQL migration / grant / schema change / EF entity.**
- **No real space picker.** Reuse `VITE_DEMO_SPACE_ID` (as P3-WP05/06/07, P4-WP02/03/04/05).

## Decisions (front-loaded — seven, all APPROVED with recommended options, 2026-07-23)

- **D-P4-IMP-SPLIT — one WP or two.** **RECOMMEND: split WP06a (backend) → WP06b (frontend)**, mirroring WP03a/b, WP04a/b, WP05a/b — frontend types come from the regenerated OpenAPI, and the combined effort exceeds ≤ 2 days. *Alternative:* one combined WP — rejected (over the ceiling; hard frontend→backend regen dependency).
- **D-P4-IMP-FORMAT — CSV vs CSV+XLSX.** **RECOMMEND: CSV-only in M1**, canonical UTF-8 RFC-4180, parsed/serialized **server-side** with a small hand-rolled reader/writer for the fixed narrow schema (zero new dependency), pinned by round-trip + quoting-edge-case unit tests. Rationale: the roadmap's self-migration path is explicitly CSV (line 5); the OLD XLSX dependency (SheetJS `xlsx`) carries prototype-pollution CVEs and would be a **new, security-sensitive** dependency on whichever side parses it. *Alternative A:* add XLSX now via a backend lib (`ClosedXML`/`DocumentFormat.OpenXml`) — deferred (new dependency + surface; not required for the migration gate). *Alternative B:* client-side XLSX→JSON with a library-free server — rejected (moves parsing off the server-authoritative path and reintroduces the CVE-bearing dep). If the user wants XLSX in M1, it becomes a recorded plan dependency and a small scope bump.
- **D-P4-IMP-EXPORT — server endpoints vs client-side serialization.** **RECOMMEND: server export endpoints** (`GET …/accounts/export`, `…/groups/export`) that emit the canonical importable columns, so the file format has a single server-side authority and the export→import round-trip is testable end-to-end in the integration tier. *Alternative:* build the CSV client-side from the existing `useAccounts`/`useAccountGroups` reads — rejected (splits format ownership across tiers; harder to pin the round-trip).
- **D-P4-IMP-BULK — dedicated bulk endpoint + transaction semantics.** **RECOMMEND: a dedicated `POST …/{accounts|groups}/import` bulk endpoint** applying rows through the WP05 write service inside **one** transaction under **one** idempotency key, **all-or-nothing** (any invalid row → roll back, `422` with the full row-indexed report; all valid → commit, `200` report). Rationale: a chart import is a migration unit; partial application would leave a half-imported chart that is hard to reason about or re-run. *Alternative A:* N client-side `POST …/accounts` calls in a loop — rejected (no atomicity, N idempotency keys, N round-trips, racey). *Alternative B:* partial apply (commit valid rows, report failures) — rejected for M1 (all-or-nothing is safer and re-runnable; can be revisited if large-file partial import is requested).
- **D-P4-IMP-UPSERT — import row semantics.** **RECOMMEND: upsert-by-natural-key** — accounts by `code` (create if the code is absent in the space; update the mutable fields if present), groups by `name`. Rationale: makes re-import idempotent and supports "export, tweak, re-import" and a fresh-space migration in one pass; on an unposted fresh space (the migration target) all edits are allowed, and on a posted account the C-P4-COA-EDIT immutability rules reject the offending row exactly as a single `PATCH` would. *Alternative:* create-only (duplicate code → row error) — rejected (breaks re-import idempotency and the round-trip on a partially-populated space).
- **D-P4-IMP-GROUP-REF — how account rows reference groups.** **RECOMMEND: accounts reference groups by name; import groups first, then accounts; an account row naming an absent group fails that row** (`422 account.group_unknown`) rather than silently auto-creating a group with a guessed code range (ranges are structural and GiST-exclusive — guessing them is unsafe). The export emits the group name per account and a separate groups file; the UI imports groups then accounts (or a single "import both" that runs groups first). *Alternative:* embed group range on each account row and auto-create — rejected (denormalized, ambiguous when rows disagree, invites overlap errors).
- **D-P4-IMP-OWNER — owner-by-email columns.** **RECOMMEND: defer application to after P4-WP11** — export omits owner columns (no ownership write path exists yet), import tolerates and **ignores** any owner/ownerEmail/owners/ownersEmail columns with a per-row warning so an OLD-app export still imports its account rows. Rationale: attribution editing + the member-email directory are P4-WP11 / P4-WP07, and applying ownership now would smuggle in an un-built write path (and a possible attribution accounting consult). *Alternative:* pull P4-WP11 in as a hard dependency and do owner-by-email now — rejected (blocks WP06 on unbuilt features; over the ceiling). Recorded as a carry-forward so the roadmap's full owner-by-email line is completed when attribution lands.

## Accounting decisions

**None.** WP06 decides no accounting behavior. Every imported row is validated by the **already-approved, fixture-pinned** P4-WP05 / P2-WP03 domain (code unique/in-range, range non-overlap, `valid_from ≤ valid_to`, currency via `CurrencyCode`) and the **C-P4-COA-EDIT** post-posting mutability/lifecycle policy (resolved 2026-07-22) — WP06 reuses them verbatim, it does not re-decide them. Money stays integer minor units + ISO currency (no amounts move in a chart import at all). The client mirrors nothing new; the server + DB constraints remain the wall. **If any import rule cannot be traced to the P4-WP05 domain, an existing fixture, or a C-P4-COA-EDIT answer, stop and route to LL Accounting Expert.** The one place accounting *could* enter — owner-by-email/attribution — is explicitly deferred (D-P4-IMP-OWNER), so no consult is triggered here.

## Golden fixtures

**None required.** WP06 ports no OLD accounting *engine*: the OLD import/export was UI/SheetJS/Dexie-computed (a §5 rewrite-not-port), and the row validation intent is already implemented and **already pinned** by the P4-WP05 write service + the 22 P2-WP01 currency/FX golden fixtures the domain factories reuse. The new surface is (a) mechanical CSV serialization/parsing — pinned by **round-trip + quoting-edge-case unit tests**, and (b) mechanical bulk-apply wiring (idempotency/authorization/RLS/audit/all-or-nothing) — pinned by **integration tests**, exactly like P3-WP05/P2-WP09/P4-WP05. The self-migration **round-trip** (export → import → catalog identical) is the load-bearing correctness test and is a test artifact, not an accounting oracle. If later research identifies a specific OLD parsing/mapping behavior that must be preserved bit-for-bit, capture it through LL Fixture Smith before changing this plan.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e` (local checkout at `C:\Programming\LeafLedger\Accounting`).

- **Rewrite (§5 data access):** `useAccountsViewModel.ts` `handleExportAccounts` (L207), `handleDownloadTemplate` (L342), `handleImportFile` (L413) — client-side SheetJS over Dexie → new server export endpoints + a server bulk-import service composing the WP05 write path + a thin upload/report UI over the generated client. The OLD `xlsx` dependency and its client parsing are **not** ported; only the column intent informs the M1 canonical CSV (truncated to the M1 field set).
- **Reuse (not re-implemented):** the P4-WP05 `IAccountManagementService` create/update/activate + group create/update (the sole write path — import applies through it), the P2-WP09 idempotency envelope, the P2-WP06 authorization + RLS/actor binding, the P3-WP05 read + `AccountView`/`GroupCatalogReport`, the P3-WP06 modal/idempotency-key frontend precedent.
- **New capability (no OLD oracle):** server-side account/group export endpoints + a bulk-import endpoint with a row-indexed report — pinned by integration + round-trip tests.
- **Deferred (columns present in OLD, not applied in M1):** owner-by-email (P4-WP11) and the M2 policy columns (cash-flow / investments / FX overrides / VAT) — tolerated-and-ignored, per D-P4-IMP-OWNER and the Non-goals.

## Dependencies

- **No new production dependency** under the recommended D-P4-IMP-FORMAT (server-side CSV is hand-rolled for the fixed schema). If the user chooses XLSX-in-M1, a backend XLSX library (e.g. `ClosedXML` or `DocumentFormat.OpenXml`) becomes a recorded plan dependency added here **before** use. No new frontend dependency (the UI posts a file/rows through the generated client; no client-side spreadsheet parsing).
- **No new migration / grant / schema change** — everything the import writes needs exists (P2-WP02); export reuses the existing RLS-bound reads.
- OpenAPI/TS regeneration uses the existing P1-WP04 pipeline (contract gate must stay green).

## File list (implementation target)

**WP06a — backend (new/modified)**
- `backend/src/LeafLedger.Modules.Ledger/Application/Accounts/AccountImportContracts.cs` — `ImportReport`/`ImportRowResult` + JSON row records + service interface.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/AccountCsv.cs` — RFC-4180 read/write.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/AccountImportService.cs` — one-transaction, idempotent, all-or-nothing bulk apply + group-name→id resolution + upsert.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/AccountCatalogService.cs` / `GroupCatalogService.cs` — reuse the RLS-bound reads for export (or a thin export service).
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerEndpoints.cs` — two export + two import routes + authorization + idempotency + ProblemDetails.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerModule.cs` — register the service(s).
- `backend/src/LeafLedger.Host/**` OpenAPI transformer — add the import operation IDs to the `Idempotency-Key` set.
- `backend/openapi/leafledger-v1.json` — regenerated.
- `app/src/api/schema.d.ts` — regenerated (generated file; regenerate, do not hand-edit).

**WP06b — frontend (new/modified)**
- `app/src/application/accountImport.ts` — export/import wrappers + idempotency + typed errors + `ImportReport` mapping.
- `app/src/application/query/useImportAccounts.ts`, `useImportGroups.ts`; `invalidationMap.ts` (extend).
- `app/src/features/accounts/ImportModal.tsx` (+ a report table component) + export buttons on `AccountsPage.tsx`/groups surface (decompose to stay ≤ 450 lines).
- `app/src/i18n/locales/en.json`, `de.json` — import/export keys (both locales; duplicate-key gate green).

**Tests**
- Backend integration (`postgres:17`): **round-trip** (seed a chart → `GET …/export` → `POST …/import` the exported bytes → catalog byte-identical, all rows `updated`/no-ops); create-path import (fresh space, groups then accounts → rows `created`, persisted + audited); **all-or-nothing** (a file with one invalid row → `422` row-indexed report, **zero** rows written); duplicate/structural errors surface the WP05 codes per row (`account.code_out_of_group_range`, `group.code_range_overlap`, unsupported currency, `account.group_unknown`); upsert (re-import updates a mutable field, leaves others); **posted-account immutability** (import editing an immutable field on a posted account → that row `422 account.field_immutable_after_posting`, whole file rolled back); **idempotency** (same file+key replays one apply + `Idempotent-Replayed: true`; same key + different file → `409`); **authorization** (export `ledger.read` incl. Viewer 200; import `accounts.manage` → anonymous 401, Member 403, Owner/Admin 2xx; cross-space RLS isolation on both); owner/policy columns present → ignored with a warning, account rows still applied; `HasPendingModelChanges()` no-drift stays green. **CSV unit tests:** quoting/escaping round-trip (comma, quote, newline, BOM, `\r\n`), empty-optional fields, header validation.
- Frontend (Vitest + Testing Library): export wrapper calls the generated export op and triggers a download; import wrapper posts the file with an `Idempotency-Key` and maps the `ImportReport`; `useImportAccounts`/`useImportGroups` invalidate `qk.accounts.list`/`qk.accountGroups.list` (+ report keys) on success; `ImportModal` renders, submits, disables-while-submitting, and renders a row-indexed report incl. inline per-row errors; a 403 surfaces the permission-denied message; UX-only pre-checks (empty file / missing header) block without bypassing the server; i18n keys present EN + DE; layering/page-budget green.
- Contract: `backend/openapi/leafledger-v1.json` + `app/src/api/schema.d.ts` regenerate consistently (CI `contract` gate green); the four operations + new schemas present.

## Acceptance criteria (concrete, testable)

1. **Export.** `GET …/accounts/export` and `…/groups/export` return deterministic, header-first CSV attachments of the RLS-scoped catalog under `ledger.read` (Viewer → 200); an integration test asserts the exact columns and stable ordering.
2. **Round-trip.** Exporting a seeded chart and importing the exact exported bytes yields a byte-identical catalog with every row reported `updated`/no-op and **zero** validation errors (the M1 self-migration round-trip for accounts).
3. **Create-path import.** On a fresh space, importing a groups file then an accounts file creates the groups and accounts (RLS-scoped, audited), each row reported `created`; a subsequent `GET …/accounts` returns them.
4. **All-or-nothing (D-P4-IMP-TXN).** An import file containing one invalid row returns `422` with a row-indexed `ImportReport` naming the failing row + code, and **no** row from that file is persisted (verified by a follow-up read).
5. **Reused validation + codes.** Per-row failures surface the existing WP05 stable codes — code outside the group range → `account.code_out_of_group_range`; overlapping group range → `group.code_range_overlap` (structured 422, never a raw 500); unsupported currency → `currency.unsupported`; an account naming an absent group → `account.group_unknown`.
6. **Upsert + posted immutability (D-P4-IMP-UPSERT + C-P4-COA-EDIT).** Re-importing with a changed mutable field updates only that field (row `updated`); an import row editing an immutable field of a **posted** account returns that row `422 account.field_immutable_after_posting` and rolls the whole file back.
7. **Idempotency (non-negotiable #4).** Import requires a valid ULID `Idempotency-Key`; a retried same-file+key replays exactly one apply with `Idempotent-Replayed: true`; same key + different file → `409 idempotency.key_reused`.
8. **Authorization + RLS.** Import is `accounts.manage`-gated (anonymous 401, Member 403, Owner/Admin 2xx); export is `ledger.read`; cross-space import/export is blocked/scoped by the RLS second wall (a second space is untouched/unreadable).
9. **Deferred columns tolerated.** An import file carrying owner/ownerEmail/owners/ownersEmail or M2 policy columns imports its account rows successfully with a per-row warning; no ownership/policy is applied (D-P4-IMP-OWNER); export omits those columns.
10. **CSV correctness.** Unit tests prove RFC-4180 round-trip for fields containing commas, quotes, and newlines, BOM tolerance, `\r\n`/`\n`, and empty optional fields.
11. **No schema/model drift + contract gate.** No EF entity/migration/grant change; `HasPendingModelChanges()` no-drift green; `backend/openapi/leafledger-v1.json` + `app/src/api/schema.d.ts` regenerated and committed; CI `contract` gate green; the four operations + new schemas present.
12. **Import/export UI.** An Owner/Admin can export accounts and groups (download) and import a CSV through a modal that renders a row-indexed report with inline per-row errors, loading/submitting states, and a 403 permission message; each page/modal ≤ 450 lines (page budget green) — proven by component tests.
13. **Layering + i18n + scope.** UI reads/writes only through `application`; no direct `fetch`/`src/api` import in `features`; ESLint boundary/layering green; all copy in the two registered locales (EN + DE, duplicate-key gate green); a scope scan confirms the diff is limited to the file list (no ownership/journal/master-data/XLSX surface).
14. **All gates green.** Backend: Release build `0/0`, architecture `3/3`, new + existing integration tests pass (Docker). Frontend: lint, typecheck, test, page-budget, build, duplicate-key check, and `npm audit --omit=dev` (no new vulnerabilities) green.

## Boundary note

WP06a keeps all EF/Npgsql + CSV parsing inside the Ledger module Infrastructure, composes the P4-WP05 write service (no re-implemented validation), and reuses the ChartOfAccounts **domain** transitively; ChartOfAccounts gains **no** Infrastructure/EF (D-P3-ACCT-MODULE still in force). WP06b respects **features → application → api**: the modal/hooks import the application wrappers (the sole `src/api` access point) and shared primitives; no generated client directly. Every import is idempotent, authorized (`accounts.manage`), RLS/actor-bound, audited, and all-or-nothing.

## Implementation notes

- **2026-07-24 — LL Frontend Dev — WP06b implemented → `verify`.** Added generated-client application wrappers for account/group CSV and typed JSON imports, idempotency keys, export downloads, TanStack Query mutations, shared catalog/report invalidation, the `ImportModal` with account/group selection, UX-only file/header checks, row-level reports, 403 handling, and AccountsPage import/export affordances. Added English/German copy and focused modal coverage. Frontend lint, typecheck, full Vitest **153/153**, page-budget, i18n/design-token checks, production build, and `git diff --check` pass (the latter retains the existing unrelated workflow line-ending warning). Next: LL QA Reviewer reviews WP06b.

- **2026-07-24 — LL QA Reviewer — WP06b QA re-review PASS → `done`.** Fresh full frontend Vitest **47 files / 164 tests**, focused backend WP06 Testcontainers/contract/endpoint/invalidation **28/28**, lint, strict typecheck, page-budget, i18n duplicate-key, design-token, production build, audit (**0 vulnerabilities**), and `git diff --check` pass. The remediation is confirmed: generated-client 422 reports render as row-level results, export transport/403 failures render friendly alerts, import reports remain visible after submission, expected catches are typed/narrow, invalidation keys are covered, and features access the API only through application wrappers. No scope, security, idempotency, RLS, generated-file, or patch-layering findings remain. WP06b is complete.

- **2026-07-24 — LL Backend Dev — Typed JSON contract remediation complete → `verify`.** Published named `AccountImportRequest`, `GroupImportRequest`, `AccountImportRow`, and `GroupImportRow` schemas with required fields and typed row properties; import request bodies reference the appropriate request schema for each operation. Added contract assertions for schema references and row properties, regenerated OpenAPI/TypeScript artifacts, and preserved runtime JSON envelope plus legacy bare-array compatibility. Focused contract **2/2**, endpoint/Testcontainers **25/25**, invalidation **1/1**, CSV **3/3**, Host Release build, OpenAPI/TS generation, and complete backend Release **390/390** pass. WP06b remains planned.

- **2026-07-24 — LL Backend Dev — JSON contract remediation complete → `verify`.** JSON imports now accept the approved `{ rows: [...] }` envelopes while retaining legacy bare-array compatibility; OpenAPI publishes JSON and CSV request media types, and `app/src/api/schema.d.ts` was regenerated. Runtime `Accepts` metadata was intentionally avoided because it reordered unauthenticated requests into `415`; the Host transformer publishes the manually parsed request contract instead. Focused JSON integration **1/1**, endpoint/Testcontainers **25/25**, invalidation **1/1**, contract **2/2**, CSV **3/3**, Host Release build, OpenAPI/TS generation, and complete backend Release **390/390** pass. `git diff --check` has only the existing unrelated workflow line-ending warning. WP06b remains planned.

- **2026-07-24 — LL Backend Dev — QA evidence gaps closed → `verify`.** Added direct Testcontainers coverage for account/group import invalidation: exactly one `accounts.list` + `accountGroups.list` enqueue per committed import, with no enqueue for failed, replayed, or colliding requests. Added import-specific endpoint coverage for anonymous `401`, Member `403`, Owner success, and cross-space import/export isolation. Focused invalidation **1/1**, endpoint/Testcontainers **24/24**, complete backend Release **389/389**, and `git diff --check` pass (apart from the existing unrelated workflow line-ending warning). No production behavior changed; WP06b remains planned.

- **2026-07-23 — LL Backend Dev — WP06a remediation complete → `verify`.** Added post-commit `accounts.list` and `accountGroups.list` invalidation enqueueing, registered the existing `IDashboardService` so the repository Release gate is green, and added focused integration coverage for group export/import, account round-trip, posted-account immutability rollback, deferred-column warnings/audit, and import key collisions. Focused endpoint/Testcontainers tests **23/23**; complete backend Release suite **387/387**; Host Release build passed; `git diff --check` reported no whitespace errors (only an existing unrelated line-ending warning). No migration, EF model, grant, permission, or production dependency added. Next: LL QA Reviewer re-reviews WP06a, then LL Frontend Dev implements WP06b.

- **2026-07-23 — LL QA Reviewer — QA re-review FAIL.** Fresh verification passes focused endpoint/Testcontainers **23/23**, CSV **3/3**, contract **2/2**, and the complete backend Release suite **387/387** (architecture **3/3**, integration **215/215**). Two evidence gaps remain: no test directly observes that a successful account/group import enqueues both catalog invalidation topics after commit, and no import-specific authorization/RLS test covers anonymous/member denial plus cross-space import/export isolation. The existing authorization test covers single-account writes and group reads, not the import routes. Next: LL Backend Dev adds these focused tests and requests re-review.

- **2026-07-23 — LL Backend Dev — WP06a implemented.** Added dependency-free RFC-4180 account/group CSV contracts and parser/serializer; deterministic account/group exports; transactional account/group imports with natural-key upsert, group-name resolution, one idempotency key, RLS/actor binding, all-or-nothing rollback, row-indexed reports, replay/collision handling, and four authorized routes. Regenerated OpenAPI/TypeScript contracts. Focused CSV tests **3/3**, contract tests **2/2**, endpoint/Testcontainers tests **18/18**, backend non-integration solution tests **224/224** (architecture included), Host Release build, frontend Vitest **151/151**, and frontend typecheck passed. No migration, EF model, grant, permission, or production dependency added.

## QA verdict

**Remediation complete — 2026-07-24 — LL Frontend Dev → `verify`.**

The three WP06b findings are addressed. Import wrappers now return a structurally valid `ImportReport` carried by a generated-client `422` error response, while non-report transport failures remain `AccountManagementError` instances. Export actions now catch expected management failures and render the existing friendly permission/server alerts. The import modal remains open after submission so row-indexed reports remain visible.

Focused remediation coverage includes wrapper/report/error behavior **5/5**, mutation-hook invalidation **2/2**, modal report/403/submitting behavior **5/5**, and AccountsPage export 403 behavior **6/6**. Full frontend Vitest is **47 files / 164 tests**; lint, strict typecheck, page budget, i18n duplicate-key, design-token, production build, audit (**0 vulnerabilities**), and diff hygiene pass. WP06b is ready for LL QA Reviewer re-review.

**PASS — 2026-07-24 — LL QA Reviewer re-review.**

1. **Acceptance criteria:** The frontend import/export workflow is complete. 422 `ImportReport` responses are returned to the modal, row-level errors and warnings render, import controls disable while submitting, successful imports keep the report visible and invalidate account/group/report queries, and export 403/server failures render friendly alerts.
2. **Executable evidence:** Full frontend Vitest **47 files / 164 tests**; focused backend WP06 Testcontainers/contract/endpoint/invalidation **28/28**; lint, strict typecheck, page-budget, i18n duplicate-key, design-token, production build, `npm audit --omit=dev` (**0 vulnerabilities**), and `git diff --check` all pass. The build emits only existing dependency annotation/chunk-size warnings.
3. **Review findings:** No remaining findings. The feature-layer dependency scan found no direct generated-client/API access; catches are limited to typed `AccountManagementError` handling; no application-generated file was hand-edited during remediation; authorization, idempotency, RLS, all-or-nothing behavior, immutability, and audit evidence remain covered by the WP06a backend gates.

WP06b is **done**. The parent P4-WP06 is complete.

**Historical FAIL — 2026-07-24 — LL QA Reviewer.** WP06b returned to `in-progress`.

1. **Blocking: row-level 422 reports are discarded by the application wrappers.** The approved frontend contract requires the `ImportReport` returned by a `422` response to render row-indexed errors in `ImportModal`. In `app/src/application/accountImport.ts`, `importAccountsCsv` and `importGroupsCsv` return only when `result.data` is defined and otherwise call `throwImportError(result)`. The generated client contract places the 422 `ImportReport` in `error`, not `data`, so the server report becomes `AccountManagementError` and `ImportModal` receives no report. The existing modal tests do not exercise this path.

2. **Major: export transport/403 failures have no user-visible handling.** The export buttons in `app/src/features/accounts/AccountsPage.tsx` call `void export...().then(...)` without a rejection handler and do not store export errors in state. Consequently a failed export produces an unhandled rejected promise and cannot render the required friendly permission message.

3. **Evidence gap: required frontend behavior is not covered by focused tests.** The current frontend suite passes **45 files / 153 tests**, but the only new import test covers empty-file validation and successful group routing. There are no focused tests for wrapper 422 mapping, export download/error behavior, import-hook invalidation, report rendering, 403 import UX, or submitting-disabled state. The backend focused WP06 gate passes **28/28** for the selected endpoint, invalidation, and contract tests; this does not close the frontend findings.

**Required next action:** LL Frontend Dev maps the generated-client 422 error body to the returned `ImportReport`, adds stateful export error handling, and adds focused wrapper/hook/modal/page tests, then requests QA re-review.

**PASS — 2026-07-24 — LL QA Reviewer re-review.**

The typed JSON contract finding is closed. OpenAPI now contains named `AccountImportRequest`, `GroupImportRequest`, `AccountImportRow`, and `GroupImportRow` schemas; both import operations reference their typed request schema, and the generated TypeScript client exposes typed row fields. Focused contract **2/2**, endpoint/Testcontainers **25/25**, invalidation **1/1**, CSV **3/3**, Host Release build, OpenAPI/TS generation, and complete backend Release **390/390** pass. WP06a is complete; WP06b remains planned.

**Historical FAIL — 2026-07-24 — LL QA Reviewer schema re-review.**

1. **Historical finding: generated import schemas were absent and JSON row fields were untyped.** The finding was resolved by publishing explicit typed schemas and regenerating both artifacts.

Executable evidence: Host Release build, focused endpoint/Testcontainers **25/25**, invalidation **1/1**, contract media-type/auth/idempotency **2/2**, CSV **3/3**, architecture **3/3**, complete backend Release **390/390**, and `npm run gen:api` pass. These results do not close the missing-schema contract finding.

**Historical FAIL — 2026-07-24 — LL QA Reviewer re-review.**

1. **Blocking: documented JSON import contract is not implemented or published.** The WP06a backend design and acceptance criteria require `POST .../groups/import` and `POST .../accounts/import` to accept either CSV or a JSON `{ rows: [...] }` body. `LedgerEndpoints.ReadAccountRowsAsync` and `ReadGroupRowsAsync` deserialize JSON as a bare `AccountImportRow[]`/`GroupImportRow[]`, so the required object shape fails with a `JsonException` rather than importing. The generated `ImportAccounts` and `ImportAccountGroups` request bodies advertise only `text/csv`, so clients cannot discover the documented JSON form. This is a runtime/API contract mismatch, not merely missing test evidence.

Executable evidence that passes: focused endpoint/Testcontainers **24/24**, import invalidation **1/1**, CSV **3/3**, contract **2/2**, complete backend Release **389/389** including architecture and integration, Host Release build, and diff hygiene (apart from an existing unrelated line-ending warning). These tests do not cover the required `{ rows: [...] }` JSON shape.

**Required next action:** LL Backend Dev either implements and documents the `{ rows: [...] }` JSON import shape, including OpenAPI/TypeScript regeneration and a focused integration test, or updates the approved plan to remove JSON support; then LL QA Reviewer re-reviews WP06a.

## Split seam (recommended execution)

- **WP06a (LL Backend Dev):** CSV read/write + export endpoints + bulk-import service (idempotent, all-or-nothing, upsert, group-name resolution) + integration/round-trip tests + OpenAPI/TS regen. Independently verifiable (export/round-trip/all-or-nothing/authorization/idempotency/RLS/immutability tests, contract gate).
- **WP06b (LL Frontend Dev):** export/import application wrappers + mutation hooks + `ImportModal`/report table + export buttons + i18n + frontend tests, against the regenerated types. Independently verifiable (component/hook/route tests, page budget).

Each half is ≤ 2 days and independently verifiable, satisfying the WP sizing rule.

## Open questions / carry-forwards

- **WP05 is complete.** PR #40 merged the WP05 write service, `accounts.manage`, `GroupCatalogReport`, and frontend accounts feature into `main` on 2026-07-23. WP06 implementation remains gated only by sign-off on the seven front-loaded WP06 decisions.
- **XLSX (D-P4-IMP-FORMAT).** If the roadmap's XLSX mention is a launch requirement, it is a small follow-up over the proven CSV path (add a backend XLSX lib, one export/import format flag) — recorded here so nothing treats CSV-only as the permanent end state.
- **Owner-by-email (D-P4-IMP-OWNER).** Completes when P4-WP11 (attribution) + P4-WP07 (member/partner email directory) land; at that point owner columns become applied on import and emitted on export, possibly with an attribution accounting consult.
- **Large-file / partial import.** M1 is all-or-nothing on a modest chart. If very large imports or partial-apply UX are requested later, that is a separate additive WP (streaming + partial report).
- **Space picker.** `VITE_DEMO_SPACE_ID` remains the slice-only stand-in until `GET /spaces` + a picker lands (shared Phase-3/4 carry-forward).
- **Candidate ADR.** The canonical CSV interchange contract (versioned header, M1 field set, deferred columns) may warrant a short ADR if the team wants the self-migration format recorded in the architecture log — a Docs Editor follow-up, not an implementation blocker.
