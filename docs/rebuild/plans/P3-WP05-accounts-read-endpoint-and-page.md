# P3-WP05 — Accounts read endpoint (backend) + dev seed + read-only accounts page

- **Phase:** 3 (frontend re-platform) — first backend read endpoint for ChartOfAccounts-owned data + first data-bound feature page.
- **State:** verify — WP05a backend acceptance and WP05b frontend implementation are complete; QA re-review passed. All six decisions below **APPROVED by the user on their recommended routes (2026-07-14)**. B1 aligned-confirmation only; B2/B3 were not re-opened (they matched/contradicted merged Phase-2 work and the user withdrew them).
- **Owner (implementation):** LL Backend Dev (WP05a) → LL Frontend Dev (WP05b). Dual-deliverable; see the split seam.
- **Depends on:**
  - **P3-WP01** (done) — app-shell router, TanStack Query provider + conventions (`qk` factory already reserves `accounts.list(spaceId)`), error boundaries, desktop-first `AppLayout`.
  - **P3-WP04** (done) — the shared `DataTable` (desktop-only), `FormSection`, tokens; the accounts page renders through `DataTable`, no new UI.
  - **P2-WP03** (done) — the ChartOfAccounts domain (typed `Account`/`AccountKind`/currency policy). The read endpoint projects the persisted account catalog; the domain is not re-invoked for a plain read.
  - **P2-WP06** (done) — the `RequireSpacePermission` authorization filter + `ledger.read` permission (granted to all roles incl. Viewer) reused by the new GET endpoint.
  - **P2-WP02** (done) — the `accounts` table + RLS isolation policy + the `leafledger_app` `SELECT` grant (already present — **no new grant/migration needed for the read**); the txn-local RLS-bound read pattern in `LedgerReportService`.
  - **P3-WP02** (done) — MSAL bearer wiring; the page's query carries the bearer token through the generated client.
- **Blocks:** **P3-WP06** (journal-entry posting flow needs an account source for its line pickers — the `AccountPicker` deferred from WP04 lands there, built on this endpoint) and, indirectly, **P3-WP07** (trial-balance page — same space-selection seam). Resolves the tracker dependency **D-P3-ACCOUNTS**.
- **Estimated size:** ≤ 2 days across two agents, at the ceiling. **Strongly recommend executing as the documented WP05a (backend) → WP05b (frontend) split** (the frontend half depends on the regenerated OpenAPI/TS types from the backend half). Backend: one read-service method + one GET endpoint + a Development-only seed + integration tests + OpenAPI/TS regen. Frontend: one feature folder + query wrapper/hook + one `AccountsPage` (through `DataTable`) + route + tests.

## Context / scope note (LL Architect)

Part 4 §Phase 3 lists "accounts … pages … against the new API" as part of the vertical slice, and Part 3 §API lists `CRUD /api/v1/spaces/{spaceId}/accounts|groups|…`. Full **Accounts CRUD / built-in groups / import-export** is **M1 = Phase 4** (roadmap §"Chart of accounts"). WP05 delivers only the **read** half the Phase-3 slice needs: a signed-in user can open an accounts page and see the space's account catalog served by the new backend, proving the read path end-to-end (MSAL bearer → generated client → TanStack Query → `DataTable`). This is the first ChartOfAccounts-facing HTTP endpoint and the first data-bound `*Page.tsx`.

**The gap this closes (tracker D-P3-ACCOUNTS):** WP06's journal-entry page needs an account source, but no `GET accounts` endpoint exists and no non-test path seeds accounts. WP05 adds the thin read endpoint **and** a Development-only demo seed so accounts actually exist locally. The recommended route is **option (a)** (thin read endpoint + dev seed), not option (b) (typed account-code entry deferring all accounts UI to Phase 4).

This is a **rewrite** WP per §5: the OLD accounts UI read Dexie via a `DataApi` — "all data access Dexie → TanStack Query + generated client" is the §5 rewrite line. The account **read projection** is a straightforward relational SELECT (no accounting rule ported); the account **presentation** reuses the WP04 `DataTable`. There is **no accounting behavior** in a read-only catalog list, so **no golden fixtures and no accounting consult** are required (see those sections). The demo seed's chart is explicitly **illustrative dev data**, not the normative built-in chart (that, with real codes/groups, is Phase 4 and will route to LL Accounting Expert then).

## Spec sources

- `docs/architecture/rebuild/03-target-architecture.md` §"Modules" (Chart of Accounts "publishes catalog to Ledger"), §API (`CRUD /api/v1/spaces/{spaceId}/accounts|groups|…` — WP05 does the **read** slice of `accounts`), §"Existing users start fresh spaces … self-serve via … import pipeline" (no data migration; fresh spaces need a seed/onboarding to be non-empty).
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 3 ("accounts + journal entry pages … against the new API, decomposed under page budgets"), §5 salvage/rewrite ("**all data access** Dexie → TanStack Query + generated client").
- `docs/architecture/rebuild/06-feature-roadmap.md` M1 "Chart of accounts" (Accounts CRUD + built-in groups + code ranges + import/export = **Phase 4**; WP05 is read-only, no CRUD/import).
- `docs/architecture/rebuild/07-vibe-coding-playbook.md` §107 (reuse shared primitives — the page uses the WP04 `DataTable`).
- `.github/instructions/frontend.instructions.md` — layering **features → application → api**; page budget 450/30/20; money is integer minor units, format at the render edge (accounts carry no amounts, but any future balance column obeys this); reuse shared primitives.
- `.github/instructions/backend.instructions.md` — module boundaries (EF stays in Infrastructure); RLS second wall; authorized read endpoints.
- `docs/rebuild/plans/P2-WP07-reporting-views-integrity.md` — the txn-local RLS-bound read pattern (`SET LOCAL ROLE leafledger_app` + `set_config('app.current_space_id', …, true)`) and the `ledger.read` authorization the accounts read reuses.
- `docs/rebuild/plans/P3-WP01-app-shell-foundation.md` / `P3-WP04-shared-ui-primitives.md` — the query-key factory (`qk.accounts.list`), TanStack Query conventions, and the `DataTable` the page renders through.

## Goal

A signed-in user can navigate to an accounts route and see the current space's account catalog, served by a new authorized backend read endpoint over the real Postgres schema, with the read path proven end-to-end. Concretely:

1. **Backend (WP05a):** `GET /api/v1/spaces/{spaceId}/accounts` returns the space's accounts (RLS-bound, `ledger.read`-authorized), ordered by code; OpenAPI/TS artifacts regenerated. A **Development-only** idempotent seed makes a demo space + illustrative chart + open period + a dev-user Owner membership exist so the endpoint returns data locally.
2. **Frontend (WP05b):** a read-only `AccountsPage` (feature `app/src/features/accounts/`) fetches via an application-layer `getAccounts()` wrapper + `useAccounts()` query hook and renders the catalog through the WP04 `DataTable`, wired to a `/accounts` route with loading/empty/error states.

WP05 adds **no** account mutation, **no** CRUD/import, **no** built-in normative chart, and **no** space-picker UI.

## Scope

### WP05a — backend read endpoint + dev seed (LL Backend Dev)

1. **Read service (Ledger Infrastructure) — `IAccountCatalogService` + `AccountCatalogService`:**
   - One method `Task<AccountCatalogReport> GetAccountsAsync(Guid spaceId, CancellationToken)` reading the `accounts` table through the **same txn-local RLS-bound connection pattern** as `LedgerReportService` (`SET LOCAL ROLE leafledger_app` + `set_config('app.current_space_id', @space, true)`; rollback-on-dispose). `SELECT id, code, name, currency, kind, is_active, group_id, valid_from, valid_to, fx_policy FROM accounts ORDER BY code, id`. Read-only; no writes; the `leafledger_app` `SELECT` grant + RLS policy already exist (P2-WP02) so **no migration** is needed.
   - Lives in the **Ledger module Infrastructure** (see D-P3-ACCT-MODULE): that is where the single `LedgerDbContext`, the `accounts` table, and the RLS-bound read plumbing already live; ChartOfAccounts is a pure SharedKernel-only domain with no EF/Infrastructure.
2. **Application contract — `Application/Accounts/AccountCatalogContracts.cs`:**
   - `AccountView(Guid Id, int Code, string Name, string Currency, string Kind, bool IsActive, Guid GroupId, DateOnly? ValidFrom, DateOnly? ValidTo, string? FxPolicy)` and `AccountCatalogReport(Guid SpaceId, IReadOnlyList<AccountView> Accounts)`, mirroring the `LedgerReportContracts` record style.
3. **Endpoint (Ledger Infrastructure endpoints) — extend `LedgerEndpoints`:**
   - `GET /api/v1/spaces/{spaceId:guid}/accounts` → `AccountCatalogReport`, `.WithName("GetAccounts")`, `.Produces<AccountCatalogReport>(200)` + 401/403 ProblemDetails, authorized with **`ledger.read`** (reused; see D-P3-ACCT-PERM) via the existing `configureAuthorization` seam. Tagged `ChartOfAccounts` (or `Accounts`) for OpenAPI grouping.
   - Register `IAccountCatalogService` in `LedgerModule.AddLedgerModule`.
4. **Development-only demo seed — `Infrastructure/DevSeed.cs` + Host wiring (D-P3-ACCT-SEED):**
   - An idempotent seeder invoked **only** when `app.Environment.IsDevelopment()` (alongside the existing dev-only `MigrateLedgerAsync()` call in `Program.cs`), gated additionally by a `Seed:Enabled` flag (default true in `appsettings.Development.json`, absent/false elsewhere). It creates, **if not already present** (keyed on a fixed demo space id): one demo space (base currency CHF), a few illustrative account groups + accounts spanning the account kinds, one open current-fiscal-year period, and an **Owner** membership for a configurable `Seed:DevUserId` (documented; the developer sets it to their resolved internal `user_id`). No-op when the demo space already exists. **Never runs in Production** (double-gated). Uses the owner/superuser connection for seeding (RLS-exempt), mirroring the test fixture inserts.
5. **OpenAPI/TS regeneration:** rebuild the Host OpenAPI doc (`backend/openapi/leafledger-v1.json`) and regenerate `app/src/api/schema.d.ts` so the frontend has typed `AccountCatalogReport`/`AccountView`. The CI `contract` gate must be green (committed artifacts match generated). No idempotency header on this read (GET).
6. **Backend tests (integration, `postgres:17`):** listed in acceptance criteria.

### WP05b — read-only accounts page (LL Frontend Dev)

7. **Application layer — `app/src/application/accounts.ts` + `query/useAccounts.ts`:**
   - `getAccounts(spaceId, client = apiClient)` wraps `client.GET('/api/v1/spaces/{spaceId}/accounts', …)` (the single point of `src/api` access, mirroring `getMeta`), maps to a frontend `Account[]` view type. `useAccounts(spaceId)` = `useQuery({ queryKey: qk.accounts.list(spaceId), queryFn: () => getAccounts(spaceId) })` (the key is already reserved in `qk`).
8. **Feature page — `app/src/features/accounts/AccountsPage.tsx` (+ small colocated bits):**
   - Read-only page: loading state, empty state (no accounts), error surfaced to the route error boundary, and a rows state rendering the catalog through the WP04 `DataTable` (columns: code, name, currency, kind, active) with i18n keys for headers/labels (add keys to the WP03 EN/DE corpus). ≤ 450 lines (page budget).
   - **Space selection for the slice (D-P3-ACCT-SPACE):** the page resolves the demo space id from a Vite env var (`VITE_DEMO_SPACE_ID`, matching the seeded demo space) — a real space-picker/`GET /spaces` is a later WP. Documented as a slice-only seam.
9. **Routing — `app/src/app/router.tsx`:** add an `/accounts` child route (lazy) under `AppLayout`; add a nav affordance in the shell.
10. **Frontend tests:** listed in acceptance criteria.

## Non-goals (explicitly deferred)

- **No account mutation / CRUD — Phase 4.** No create/edit/deactivate/delete accounts; no groups CRUD; no code-range editing. WP05 is read-only.
- **No import/export — Phase 4.** No CSV/XLSX account import (incl. owner-by-email) or export; the self-migration pipeline is Phase 4.
- **No normative built-in chart of accounts — Phase 4.** The demo seed is **illustrative dev data**, not a shipped standard chart. The real built-in groups/codes (an accounting artifact) land in Phase 4 and route to LL Accounting Expert then.
- **No space-creation / onboarding wizard — later (Phase 3 onboarding / Phase 4).** The dev seed is a stand-in so the slice works locally; it is not the production onboarding flow. Real membership provisioning stays link-only per P2-WP13.
- **No real space picker / `GET /spaces` — later.** The slice uses a configured demo space id (`VITE_DEMO_SPACE_ID`).
- **No account balances / drill-down on the page.** The accounts page lists the catalog only; balances are the trial-balance report (WP07). If a balance column is ever added it obeys the integer-minor-units render-edge rule.
- **No new module (ChartOfAccounts gains no Infrastructure/EF).** Per D-P3-ACCT-MODULE the read lives in Ledger Infrastructure; ChartOfAccounts stays pure domain.
- **No pagination/filtering/search.** Return all accounts for the space ordered by code (M1 spaces are small; Phase 4 adds list controls).

## Decisions (front-loaded, non-accounting — all APPROVED by the user 2026-07-14 on their recommended routes)

All six decisions below were approved on their recommended routes; each is now an implementation constraint for LL Backend Dev / LL Frontend Dev.

- **D-P3-ACCOUNTS — the tracker decision. RECOMMEND (a): thin read-only `GET …/accounts` + a Development-only demo seed**, over (b) typed account-code entry deferring all accounts UI to Phase 4. Rationale: (a) proves the real read path end-to-end (the Phase-3 point), gives WP06 a real account source for its picker, and is a small relational projection; (b) leaves the read path unproven and pushes an awkward raw-code UX into WP06. Recommend (a).
  - *Alternative (b):* defer accounts UI entirely, enter account codes as free text in WP06. Rejected — unproven read path, worse posting UX, still needs a seed for codes to be valid.
- **D-P3-ACCT-MODULE — where the read endpoint lives. RECOMMEND: implement in the **Ledger module Infrastructure** (route `/api/v1/spaces/{spaceId}/accounts`, OpenAPI tag `ChartOfAccounts`).** The `accounts` table, the single `LedgerDbContext`, and the RLS-bound read plumbing already live in the Ledger module (the D1 single-context decision, P2-WP02); ChartOfAccounts is deliberately pure-domain (SharedKernel-only, zero EF). Adding Infrastructure/EF to ChartOfAccounts — or making it depend on Ledger — inverts the §"publishes catalog to Ledger" direction (Ledger depends on ChartOfAccounts). Keeping the read in Ledger Infrastructure is the minimal, boundary-respecting path. Flag as a small precedent (candidate note, not a full ADR): "read endpoints over CoA-owned tables live in Ledger Infrastructure until/unless ChartOfAccounts gets its own persistence."
  - *Alternative:* give ChartOfAccounts its own DbContext/Infrastructure now → premature module-persistence split, boundary inversion, exceeds ≤2 days. Rejected for WP05.
- **D-P3-ACCT-SEED — how accounts come to exist locally. RECOMMEND: a Development-only, double-gated (`IsDevelopment()` + `Seed:Enabled`), idempotent demo seed** (fixed demo space + illustrative chart + open period + Owner membership for a configured `Seed:DevUserId`), never in Production. It unblocks the whole Phase-3 vertical slice locally without building the real onboarding flow.
  - *Sub-question (dev-user membership):* the seeded Owner membership is keyed to a **configurable** `Seed:DevUserId` the developer sets to their resolved internal `user_id` (post P2-WP13 identity-links). Recommended over auto-provisioning membership on first authenticated request (that couples the seed to identity resolution and risks re-introducing the auto-join class WP13 removed). Documented as an open question for the live-sign-in smoke (WP08 exit gate) if a smoother dev linkage is wanted.
  - *Alternative:* a real space-creation endpoint/onboarding wizard → out of scope, larger than the WP. Rejected for WP05.
- **D-P3-ACCT-SPACE — space selection for the slice. RECOMMEND: the frontend reads a configured demo space id from `VITE_DEMO_SPACE_ID`** (matching the seeded demo space); a real space picker / `GET /spaces` is a later WP. Keeps WP05b a single read page without a spaces-list endpoint.
  - *Alternative:* build `GET /spaces` + a picker now → scope creep into space management. Rejected for WP05.
- **D-P3-ACCT-PERM — authorization for the read. RECOMMEND: reuse the existing `ledger.read` permission** (already granted to all roles incl. Viewer, P2-WP06/WP07) rather than minting a new `accounts.read`. A read of the account catalog is a space read; reusing `ledger.read` avoids new permission plumbing and matches the reports.
  - *Alternative:* new `accounts.read` permission → extra plumbing for no access-control benefit at M1. Rejected (revisit if Phase-4 CRUD needs finer scopes).
- **D-P3-ACCT-DTO — read shape. RECOMMEND: a lean `AccountView`** (`id, code, name, currency, kind, isActive, groupId, validFrom?, validTo?, fxPolicy?`) — enough for the list and for WP06's picker, nothing derived. Minor; folded into scope.
  - *Alternative:* include group name / computed fields via a join → more query surface than the slice needs. Rejected for WP05 (add in Phase 4 CRUD).

## Accounting decisions

**None required.** A read-only account catalog list carries no accounting behavior — it is a relational projection of already-persisted rows. No LL Accounting Expert consult for WP05. **Two boundaries are respected, not decided:** (1) money stays integer minor units + ISO currency and is formatted only at the render edge (the accounts list has no amount column at M1; any future balance column obeys this existing rule); (2) the **demo seed's chart is illustrative dev data, not a normative built-in chart** — the real built-in groups/codes/kinds are an accounting artifact deferred to Phase 4, which **will** route to LL Accounting Expert. Recorded so a later WP does not treat the demo codes as authoritative.

## Golden fixtures

**None required.** WP05 ports no accounting rule and computes no financial value; it projects persisted account rows and lists them. Correctness is pinned by **integration tests** (the endpoint returns the seeded accounts, RLS-scopes them per space, and enforces `ledger.read`) and **frontend component tests** (loading/empty/error/rows), which are the correct tiers. The client-side validation mirroring that needs the P2-WP01/WP04 golden fixtures begins in **WP06** (the posting flow), not here.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e` (local checkout at `C:\Programming\LeafLedger\Accounting`).

- **Rewrite (§5 data access):** the OLD accounts list page + its Dexie `DataApi` account reads → a new `getAccounts()` over the generated client + TanStack Query `useAccounts()` hook. The OLD read path (IndexedDB/Dexie) is **not** ported; only the *page structure/columns* inform the new page, which renders through the WP04 `DataTable`.
- **New capability (no OLD oracle):** `GET /api/v1/spaces/{spaceId}/accounts` and the RLS-bound account read service — server-side account catalog read is a new backend capability (the OLD app read accounts client-side from Dexie), pinned by integration tests, not a captured oracle.
- **New (dev tooling):** the Development-only demo seed — no OLD equivalent (the OLD app had its own client-side onboarding). Illustrative, Development-gated.
- **Reuse (WP04):** `DataTable`, `FormSection`, tokens; the WP03 i18n corpus (add account-page keys, EN + DE).

## Dependencies

- **No new production dependency** expected — backend uses existing Npgsql/EF plumbing; frontend uses existing React/TanStack Query/generated client. Any addition is a plan amendment recorded here before use.
- **No new migration** — the `accounts` `SELECT` grant + RLS policy already exist (P2-WP02); the read is a query, the seed uses the owner connection.
- OpenAPI/TS regeneration uses the existing P1-WP04 pipeline (must stay contract-gate green).

## File list (implementation target)

**WP05a — backend (new/modified)**
- `backend/src/LeafLedger.Modules.Ledger/Application/Accounts/AccountCatalogContracts.cs` — `IAccountCatalogService`, `AccountView`, `AccountCatalogReport`.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/AccountCatalogService.cs` — RLS-bound account read.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerEndpoints.cs` — add `GET …/accounts` (`ledger.read`, `Produces<AccountCatalogReport>(200)` + 401/403).
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerModule.cs` — register `IAccountCatalogService`.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/DevSeed.cs` + `LeafLedger.Host/Program.cs` — Development-only, double-gated idempotent seed wiring.
- `backend/src/LeafLedger.Host/appsettings.Development.json` — `Seed:Enabled`, `Seed:DevUserId`, demo space id config.
- `backend/openapi/leafledger-v1.json` — regenerated.
- `app/src/api/schema.d.ts` — regenerated (generated file; regenerate, do not hand-edit).

**WP05b — frontend (new/modified)**
- `app/src/application/accounts.ts` — `getAccounts()` + `Account` view type.
- `app/src/application/query/useAccounts.ts` — `useAccounts(spaceId)` hook.
- `app/src/features/accounts/AccountsPage.tsx` — read-only accounts page (through `DataTable`).
- `app/src/app/router.tsx` — `/accounts` route + nav affordance (`AppLayout`).
- `app/src/i18n/locales/en.json`, `de.json` — account-page keys (both locales, duplicate-key gate green).
- `app/.env`-style Vite config note for `VITE_DEMO_SPACE_ID` (documented; no secret).

**Tests**
- Backend integration (`postgres:17`): endpoint returns seeded accounts ordered by code; RLS scopes accounts to the space (a second space's accounts are hidden); `ledger.read` enforced (401 anonymous / 403 without permission, 200 for a Viewer); empty space returns an empty list; the seed is idempotent (running twice leaves one demo space) and Development-gated (no-op logic when disabled). No-model-drift (`HasPendingModelChanges()`) stays green (no EF entity/model change — raw SQL read + seed).
- Frontend (Vitest + Testing Library): `getAccounts()` maps the generated response; `useAccounts` query wiring; `AccountsPage` loading, empty, error (boundary), and rows states through `DataTable`; the route resolves `/accounts`; i18n keys present in EN + DE.
- Contract: `backend/openapi/leafledger-v1.json` + `app/src/api/schema.d.ts` regenerate byte-consistent (CI `contract` gate green); `GET /api/v1/spaces/{spaceId}/accounts` + `AccountCatalogReport` present.

## Acceptance criteria (concrete, testable)

1. **Read endpoint exists and is correct.** `GET /api/v1/spaces/{spaceId}/accounts` returns the space's accounts as `AccountCatalogReport` ordered by code; an integration test seeds ≥2 accounts and asserts the exact ordered projection (code, name, currency, kind, isActive, groupId).
2. **Tenancy (RLS second wall).** Accounts are scoped to `spaceId` via the txn-local RLS-bound connection; a test with two seeded spaces confirms space A's request never returns space B's accounts.
3. **Authorization.** The endpoint is `ledger.read`-gated: anonymous → 401, a principal without `ledger.read` → 403, a **Viewer** member → 200; proven by tests reusing the WP06 auth harness.
4. **Empty space.** A space with no accounts returns `200` with an empty `Accounts` list (not 404/500).
5. **Development-only idempotent seed.** With `IsDevelopment()` + `Seed:Enabled`, the seed creates the demo space + illustrative chart + open period + `Seed:DevUserId` Owner membership; running it twice leaves exactly one demo space (idempotent); it is a no-op / not wired outside Development (double-gated) — proven by tests and by the Production wiring being unreachable.
6. **No schema/model drift.** No EF entity or model change; `HasPendingModelChanges()` no-drift test stays green; no new migration is introduced.
7. **Contract regenerated and gate-green.** `backend/openapi/leafledger-v1.json` and `app/src/api/schema.d.ts` are regenerated and committed; the CI `contract` gate is green; the new path + response schema are present.
8. **Accounts page renders the catalog.** `AccountsPage` shows a loading state, an empty state, an error surfaced to the route error boundary, and a rows state rendering seeded accounts through the WP04 `DataTable` (code/name/currency/kind/active columns) — proven by component tests; the page is ≤ 450 lines (page budget green).
9. **Layering + query conventions.** The page reads only through `application` (`getAccounts`/`useAccounts` over the generated client); `useAccounts` uses `qk.accounts.list(spaceId)`; no direct `fetch`/`src/api` import in `features`; ESLint boundary + layering green.
10. **i18n.** Account-page copy uses i18n keys present in **both** EN and DE; the duplicate-key build gate is green.
11. **No out-of-scope surface.** No account mutation/CRUD/import endpoint or UI; no built-in normative chart; no space-picker/`GET /spaces`; no ChartOfAccounts EF/Infrastructure; a scope scan confirms the diff is limited to the file list.
12. **All gates green.** Backend: Release build `0/0`, architecture `3/3`, the new + existing integration tests pass (Docker). Frontend: `npm run lint`, `npm run typecheck`, `npm test`, `npm run check:page-budget`, `npm run build`, duplicate-key check, and `npm audit --omit=dev` (no new vulnerabilities) green.

## Boundary note

WP05a keeps all EF/Npgsql inside the Ledger module Infrastructure (module-boundary rule); ChartOfAccounts gains no Infrastructure/EF (D-P3-ACCT-MODULE). WP05b respects **features → application → api**: `AccountsPage` (feature) imports the `useAccounts`/`getAccounts` application layer, which is the sole `src/api` access point; the page imports the WP04 shared primitives (allowed) and no generated client directly. The dev seed writes only in Development, double-gated, never in Production.

## Split seam (recommended execution)

Because the frontend types come from the backend's regenerated OpenAPI, execute sequentially and, if the combined effort exceeds ≤ 2 days, formally split the tracker row:

- **WP05a (LL Backend Dev):** read service + `GET …/accounts` + dev seed + integration tests + OpenAPI/TS regen. Independently verifiable (endpoint + seed tests, contract gate). **Do this first.**
- **WP05b (LL Frontend Dev):** `getAccounts`/`useAccounts` + `AccountsPage` + route + i18n + frontend tests, against the regenerated types. Independently verifiable (component/route tests, page budget).

Each half is ≤ 2 days and independently verifiable, satisfying the WP sizing rule.

## Open questions / carry-forwards

- **Dev-user membership linkage (D-P3-ACCT-SEED sub-question).** The seeded Owner membership uses a configured `Seed:DevUserId`; a smoother live-sign-in linkage (resolving the signed-in Entra identity to the demo membership) is deferred to the WP08 exit-gate smoke. Recorded so WP08 planning can decide whether a dev onboarding shim is wanted.
- **Real space selection.** `VITE_DEMO_SPACE_ID` is a slice-only stand-in; `GET /spaces` + a space picker (and multi-space UX) is a later WP — flagged for WP06/WP07/Phase-4 planning so it is designed once, not duplicated.
- **Built-in normative chart of accounts (Phase 4).** The demo seed's codes/groups are illustrative; the canonical built-in chart (codes, groups, kinds, FX defaults) is an accounting artifact for Phase 4 and **will** route to LL Accounting Expert. Recorded so Phase-4 Accounts CRUD does not treat the demo seed as normative.
- **ChartOfAccounts persistence split.** If a later phase gives ChartOfAccounts its own DbContext, the read endpoint may move out of Ledger Infrastructure; D-P3-ACCT-MODULE records the current pragmatic placement and the trigger to revisit.
- **Account picker for WP06.** The deferred `AccountPicker` (WP04 → WP06) consumes this endpoint; WP06 planning should reuse `useAccounts`, not re-fetch.

## QA verdict

**PASS — WP05a backend slice (2026-07-14).**

1. The account catalog route, DTOs, RLS-bound read service, and `ledger.read` authorization are implemented within the approved Ledger Infrastructure scope.
2. Docker-backed focused acceptance passes **6/6**: ordered projection, anonymous rejection, missing-scope rejection, Viewer access, empty-space response, cross-space RLS isolation, and Development seed idempotence.
3. Ledger unit tests pass **86/86**; architecture boundaries pass **3/3**; Host Release build, frontend typecheck, duplicate-key check, production build, and `git diff --check` pass.
4. OpenAPI and TypeScript artifacts regenerate deterministically and contain `GetAccounts`, `AccountCatalogReport`, and `AccountView`.
5. Financial integrity and security review found no WP05a defect: the endpoint is read-only, uses integer-free catalog data, does not alter posting invariants, and the seed is only invoked from the Development startup branch with `Seed:Enabled` required. No new migration, EF model, ChartOfAccounts Infrastructure, secret, or generic error-swallowing layer was introduced.

The parent WP05 remains `verify` because WP05b (frontend accounts page) is a separate pending slice; this verdict clears only WP05a for the next repository workflow step.

**QA FAIL — WP05b frontend slice (2026-07-14).**

1. **Missing query-hook acceptance coverage.** `app/src/application/query/useAccounts.ts` implements the required `qk.accounts.list(spaceId)` query and `getAccounts` query function, but no test renders or invokes `useAccounts` to assert either contract. The existing `queryKeys.test.ts` only tests the key factory, while `AccountsPage.test.tsx` mocks the hook entirely. Add focused hook coverage for the query key and query function before re-review.
2. **Missing route and error-boundary acceptance coverage.** `app/src/app/router.tsx` contains the lazy `/accounts` route, but no test resolves that route through the application router. The page's error test only asserts that `AccountsPage` throws when rendered directly; it does not prove that the route's `RouteErrorBoundary` receives and renders the failure. Add a router-level test covering `/accounts` resolution and the rendered route error fallback.

The implementation passes the executable frontend gates: full Vitest **46/46**, lint, typecheck, strict page budget, duplicate-key check, production build, `npm audit --omit=dev` (**0 vulnerabilities**), and `git diff --check`. The failure is limited to the two required WP05b evidence gaps above; WP05a remains covered by the prior QA PASS. State → `in-progress`; next action is LL Frontend Dev adding the focused hook and router/error-boundary tests, then LL QA Reviewer re-review.

**QA re-review PASS — WP05b frontend slice (2026-07-14).** The two findings are closed. Direct `useAccounts` coverage now verifies the space-scoped query key and `getAccounts` query invocation; router-level coverage now resolves the lazy `/accounts` route and verifies the real `RouteErrorBoundary` fallback on query failure. Full frontend Vitest passes **49/49**; lint, typecheck, strict page budget, duplicate-key check, production build, `npm audit --omit=dev` (**0 vulnerabilities**), and `git diff --check` pass. Generated OpenAPI/TypeScript artifacts remain unchanged by the frontend remediation. State → `verify`; next action is LL Git handling.

## Implementation log

- **LL Architect (draft):** Plan drafted → state `planned` (awaiting user approval). Researched Part 3 §Modules/§API (Chart of Accounts publishes catalog to Ledger; `CRUD …/accounts` — WP05 = read slice), Part 4 §Phase 3 + §5 (all data access = rewrite → TanStack Query + generated client), roadmap M1 (Accounts CRUD/import/built-in chart = **Phase 4**), and playbook §107 (reuse `DataTable`). Inspected the real code: the `accounts` table + RLS isolation policy + `leafledger_app` `SELECT` grant already exist (P2-WP02 `InitialLedgerSchema`) so the read needs **no migration**; the txn-local RLS-bound read pattern (`LedgerReportService.OpenBoundConnectionAsync`) and endpoint/authorization seam (`LedgerEndpoints` + `configureAuthorization` + `ledger.read`) are reusable; ChartOfAccounts is pure SharedKernel-only domain (no EF/Infrastructure) so the endpoint belongs in Ledger Infrastructure; the `qk` factory already reserves `accounts.list(spaceId)`; `getMeta`/`useMeta` set the application-layer wrapper + hook precedent; the seed shape follows the integration `LedgerDbFixture.SeedSpaceAsync` inserts; `Program.cs` already has a Development-only `MigrateLedgerAsync()` gate the seed can join. **Confirmed spec-derived rewrite of data access + a new server read capability + a dev-only seed — no golden fixtures, no accounting consult** (the demo chart is illustrative; the normative built-in chart is Phase 4 → expert then). Six front-loaded non-accounting decisions surfaced for user sign-off: **D-P3-ACCOUNTS** (recommend (a) thin read endpoint + dev seed — the tracker decision), **D-P3-ACCT-MODULE** (endpoint in Ledger Infrastructure, ChartOfAccounts stays pure domain), **D-P3-ACCT-SEED** (Development-only double-gated idempotent demo seed), **D-P3-ACCT-SPACE** (`VITE_DEMO_SPACE_ID` demo space, defer real picker), **D-P3-ACCT-PERM** (reuse `ledger.read`), **D-P3-ACCT-DTO** (lean `AccountView`). Recommended executing as the WP05a (backend) → WP05b (frontend) split. 12 concrete ACs on the endpoint/RLS/authz/seed/contract/page/layering/i18n tiers. **WP state: `planned` (awaiting user approval of the decisions).**
- **LL Architect (approval):** User approved all six decisions (**D-P3-ACCOUNTS**, **D-P3-ACCT-MODULE**, **D-P3-ACCT-SEED**, **D-P3-ACCT-SPACE**, **D-P3-ACCT-PERM**, **D-P3-ACCT-DTO**) on their recommended routes and approved the plan (2026-07-14). No overrides. Note: the user had initially pasted approvals for the Phase-2 accounting decisions B1/B2/B3; LL Architect flagged that B2 ("no gaps") and B3 ("gapless, per fiscal year") **contradicted already-merged, QA-passed Phase-2 behavior** (P2-WP05 monotonic/never-reused/no-fiscal-partition entry_no with gaps permitted; P2-WP10 gaps permitted) — the user withdrew them and confirmed the intent was to approve P3-WP05. B1 (client-supplied base amount, server-validated) was already the merged behavior. Decisions are now implementation constraints; the plan is unblocked. State stays `planned`, ready for LL Backend Dev (WP05a).
- **LL Backend Dev (2026-07-14):** Implemented WP05a: RLS-bound `IAccountCatalogService`/`AccountCatalogService`, `GET /api/v1/spaces/{spaceId}/accounts` with `ledger.read`, Development-only double-gated idempotent demo seed, Host wiring, and regenerated OpenAPI/TypeScript artifacts. Focused Docker-backed acceptance **6/6** passes (ordered projection, anonymous/scope authorization, Viewer access, empty space, cross-space RLS, and seed idempotence). Release Host build, architecture **3/3**, frontend Vitest **40/40**, lint, typecheck, duplicate-key check, production build, and diff hygiene pass. State → `verify`; next LL QA Reviewer.
- **LL Frontend Dev (2026-07-14):** Implemented WP05b: application-layer `getAccounts()` wrapper over the generated client, `useAccounts()` with `qk.accounts.list(spaceId)`, configured `VITE_DEMO_SPACE_ID` resolution, read-only `AccountsPage` through the shared `DataTable`, loading/empty/rows/error states, lazy `/accounts` route, shell navigation, EN/DE i18n keys, and focused wrapper/page tests. Focused frontend tests **6/6**; full frontend Vitest **46/46**, lint, typecheck, page budget, duplicate-key check, and production build pass. State → `verify`; next LL QA Reviewer.
- **LL QA Reviewer (2026-07-14):** **QA FAIL for WP05b.** Full frontend Vitest **46/46**, lint/typecheck/page-budget/build/duplicate-key/audit/diff gates pass. Findings: missing direct `useAccounts` query-wiring coverage; missing router-level `/accounts` resolution and `RouteErrorBoundary` coverage. State → `in-progress`; next LL Frontend Dev adds those focused tests.
- **LL Frontend Dev (2026-07-14):** Closed the QA evidence findings with direct `useAccounts` query-key/query-function coverage and router-level lazy `/accounts` plus route-error-boundary tests. Full frontend Vitest **49/49**, lint/typecheck/page-budget/build/duplicate-key/audit/diff gates pass; generated contracts unchanged. State → `verify`; next LL QA Reviewer re-review.
- **LL QA Reviewer (2026-07-14):** **QA re-review PASS for WP05b.** Both findings closed. Independently reproduced focused remediation **3/3** and full frontend Vitest **49/49**, lint/typecheck/page-budget/build/duplicate-key/audit/diff gates. State remains `verify`; next LL Git handling.
