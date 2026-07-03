# LeafLedger Rebuild Analysis — Part 3: Proposed Target Architecture (Online-First + PostgreSQL)

> **Decision (2026-07-03):** drop offline-first writes and Cosmos DB; keep React/TypeScript; rebuild as a **greenfield project** with the server as the single source of truth. This supersedes the earlier offline-first commit-model draft.
>
> Design goals, in priority order:
> 1. **Financial integrity by construction** — ACID posting, database-enforced invariants, immutable audit trail
> 2. **Radical simplification** — delete the entire replication problem class instead of solving it
> 3. **Ten-year maintainability** — domain layer, generated contracts, automated gates

## 1. Architectural stance

The server owns all state. The client becomes a thin, fast, cache-aware React app. Everything that made the old system dangerous — non-atomic change capture, wall-clock sequencing, LWW merges, gap-fill, conflict UX — ceases to exist as a category.

```
┌────────────────────────── Browser (React SPA, installable shell) ──────────────────────────┐
│ features/* (UI)                                                                             │
│   └─ application/ (view-models, mutations, queries — TanStack Query)                       │
│        └─ api/ (GENERATED OpenAPI client + types — single contract)                        │
│ shared/ (UI primitives, i18n ×5, formatting)   light TS pre-validation (UX only)           │
│ PWA: manifest + static-shell precache ONLY (no data sync, no periodicsync, no OPFS)        │
└──────────────┬──────────────────────────────────────────────────────────────────────────────┘
               │ HTTPS (JWT bearer, idempotency keys on writes)          ▲ SignalR invalidation pings
┌──────────────▼──────────────────────────────────────────────┐  ┌──────┴───────────────┐
│ LeafLedger.Api — modular monolith (net9, minimal APIs)      │  │ Azure SignalR Service │
│  Modules: Ledger | ChartOfAccounts | Subledgers | Budgets   │  └──────────────────────┘
│           Imports | Documents(OCR/QR) | Spaces | Licensing  │
│           Attachments | MarketData | Integrations(KNX…)     │
│  Each: Domain / Application / Infrastructure / Endpoints    │
│  SharedKernel: Money(minor units), AccountCode, Period, Ids │
│  Middleware: auth → license entitlement → space role → rate │
└──────┬──────────────────┬──────────────────┬────────────────┘
┌──────▼───────────┐ ┌────▼─────────┐ ┌──────▼──────────────────┐
│ PostgreSQL        │ │ Azure Blob   │ │ Azure AI Document        │
│ (Flexible Server) │ │ attachments  │ │ Intelligence (OCR,       │
│ EF Core + Npgsql  │ │ (SAS grants) │ │ Swiss QR-bill)           │
│ RLS per space     │ └──────────────┘ └─────────────────────────┘
└──────────────────┘
```

## 2. Full stack review — keep / drop / replace

| Element (current) | Verdict | Target & rationale |
|---|---|---|
| React 19 + TS strict, react-router 7 | **Keep** | Proven; salvages UI primitives, feature policy, page structure |
| i18next + 5-locale corpus | **Keep** | Years of translated domain vocabulary; duplicate-key check stays in build |
| recharts, pdfjs-dist | **Keep** | Adequate; revisit only on measured need |
| Dexie/IndexedDB as source of truth | **Drop** | TanStack Query cache + server truth. No local schema, no 58 migrations |
| Entire sync subsystem (SyncEngine, changeLog, entitySeqs, gap-fill, conflicts) | **Drop** | The category disappears online-only |
| Cosmos DB (7 containers) | **Drop → PostgreSQL** | ACID across entry+lines+attributions, FK/CHECK constraints, SQL reporting, PITR backups, ~10× cheaper at this scale |
| Client-side posting engine (TS) | **Move to C# (authoritative)** | Server must be the validator. Thin TS pre-validation retained for instant UX; both sides verified against the same golden fixtures |
| MSAL / Entra `common` | **Keep** | Cache moved to `sessionStorage`; identity links table replaces runtime candidate guessing |
| Self-hosted SignalR + in-memory tracker | **Replace → Azure SignalR Service** | Scale-out safe; demoted to *cache-invalidation pings* (client refetches via React Query) — no data over the socket |
| Blob SAS attachment pattern | **Keep** | One of the best-designed parts of the old system; port nearly verbatim |
| OPFS attachment bytes | **Drop** | Direct SAS upload/download; no local byte store |
| tesseract.js client OCR | **Replace → Azure AI Document Intelligence** | Far better receipt/invoice extraction; enables **Swiss QR-bill (QR-Rechnung) ingestion** — a flagship product improvement for the Swiss market |
| `xlsx@0.18.5` (known CVEs) | **Replace** | `exceljs` (xlsx write/read) + `papaparse` (CSV); import *logic* (mapping, automation rules) ports unchanged |
| Vite 8 **beta** + patched `vite-plugin-pwa` + patch-package | **Replace** | Latest stable Vite; PWA reduced to manifest + static-shell precache (installability without sync machinery); no source patches without ADR + expiry |
| Workbox periodicsync / Web Push plumbing | **Drop / defer** | Push notifications return later as a server-driven feature if needed |
| Service-worker data logic | **Drop** | Shell caching only |
| Hand-rolled HS256 license JWT + client-side gating + dead TS function | **Replace** | Server-side entitlement middleware per request; licensing table in Postgres; client only renders what the server grants |
| Roles as magic strings, client-side rights tables | **Replace** | Typed roles (Owner/Admin/Member/Viewer) + per-module permissions enforced in endpoint filters; client mirrors server truth |
| `/maintenance/*` cross-tenant wipes | **Replace** | Per-space, platform-admin-only, soft-delete (30-day tombstone), audited |
| FX/investment price hosted services | **Keep (shape)** | Same hosted-service pattern against Postgres; AlphaVantage/OpenFIGI behind ports |
| Manual `dotnet publish` → POSIX zip → `az webapp deploy` | **Replace** | GitHub Actions → staging slot → health/integrity probe → swap; zips built on Linux runners |
| App Service B1 | **Keep initially** | Fine for current load; containerize (Dockerfile) from day one so Container Apps is a config change, not a project |
| Vercel (frontend), VitePress docs, Docusaurus help | **Keep** | Docs repo un-nested into the monorepo |
| vitest, Playwright, xunit | **Keep** | Playwright grows 1 → ~15 specs; xunit gains Testcontainers-Postgres integration tier |
| ESLint boundary rules + page-budget ratchet | **Keep, promote to blocking CI** | The discipline that worked, now enforced |
| `Accounting_docs/` duplicate app | **Drop** | Archive; single app |
| Committed `publish/`, `temp-*.py`, artifacts | **Drop** | CI artifacts + ops runbooks instead |

**New capabilities the old stack couldn't offer:** database-enforced balance invariants, cross-entity SQL reporting (trial balance as a view), Row-Level Security as tenancy defense-in-depth, point-in-time recovery, server-computed dashboards (no derived-state drift), Swiss QR-bill scanning, honest license enforcement.

## 3. Domain boundaries (bounded contexts)

Unchanged in spirit from the audit — the contexts were right, their enforcement wasn't:

| Context | Owns | Notes |
|---|---|---|
| **Ledger** (core) | Journal entries, lines, attributions, periods, close, FX revaluation, trial balance | Sole writer of postings; posted entries **immutable** — corrections are reversal + new entry |
| **Chart of Accounts** | Accounts, groups, code ranges, ownership, FX policy | Publishes catalog to Ledger |
| **Subledgers** | Real estate, investments, VAT | Generate *proposed* entries; post through Ledger commands |
| **Budgets & Reporting** | Budgets, dashboards, pivots | SQL views/materialized views — server-computed, checksummable |
| **Imports** | CSV/XLSX/bank parsing, staging, automation rules | Staging is server-side per user+space (survives device changes — an upgrade over today) |
| **Documents** | OCR, QR-bill extraction, attachment metadata | Document Intelligence adapter behind a port |
| **Spaces & Identity** | Spaces, memberships, roles, invitations, identity links | Server-authoritative |
| **Licensing** | Plans, entitlements, devices | Middleware-enforced |
| **Market Data** | FX rates, prices | ACL to AlphaVantage/OpenFIGI/rate sources |
| **Integrations** (future) | KNX/webhooks, bank APIs (EBICS/camt.053), e-invoicing | One-way dependency into Imports; core never references it |

## 4. Data model (PostgreSQL)

### 4.1 Principles
- **IDs:** prefixed ULIDs (`je_…`, `acc_…`) stored as `text`/`uuid`-compatible; sortable, collision-free, no numeric-id ↔ global-id impedance ever again.
- **Money:** `bigint` minor units + `char(3)` currency. **No floats anywhere near amounts.** Rendering conversion at the edge.
- **Tenancy:** every business table carries `space_id` FK; **Row-Level Security** policies bind `space_id` to a session variable set per request — even a buggy query cannot cross tenants.
- **Immutability:** `journal_entries`/`journal_lines` are append-only (no UPDATE/DELETE grants for the app role on posted rows); corrections via reversal linkage (`reverses_entry_id`).
- **Audit:** trigger-maintained `audit_log` (who/when/what/before/after) on all mutable tables; posted journal rows *are* their own audit record.

### 4.2 Core schema sketch

```sql
journal_entries(id, space_id, entry_no, date, status, description, reference,
                reverses_entry_id NULL, created_by, created_at, …)
journal_lines(id, entry_id FK, account_id FK, amount_minor BIGINT,  -- signed: +debit / −credit
              currency, base_amount_minor BIGINT, fx_rate NUMERIC(18,8),
              vat_code_id NULL, business_partner_id NULL, project_id NULL, …)
line_attributions(id, line_id FK, user_id FK, share_permille INT)   -- sum = 1000 enforced

-- The invariant the old system never had, enforced by the database itself:
CREATE CONSTRAINT TRIGGER trg_entry_balanced
  AFTER INSERT OR UPDATE ON journal_lines
  DEFERRABLE INITIALLY DEFERRED            -- checked at COMMIT of the posting transaction
  FOR EACH ROW EXECUTE FUNCTION assert_entry_balanced();  -- SUM(base_amount_minor) = 0 per entry
```

Accounts, groups (with code ranges as `int4range` + exclusion constraints), periods (open/closed with posting-date FK checks), VAT codes/periods, subledger tables, budgets — all straightforward relational ports of the audited Dexie model, with FKs replacing the "stage-7 order is non-critical" non-guarantee.

### 4.3 Reporting
`trial_balance`, `balance_sheet_lines`, `income_statement_lines`, `vat_report` as SQL views (materialized where hot, refreshed on posting). `GET /spaces/{id}/integrity` returns a trial-balance hash — still the convergence/monitoring primitive, now trivially computed.

### 4.4 Concurrency
- Master data: optimistic concurrency via `xmin`/rowversion → 409 with current state (React Query refetch + merge UI for the rare case).
- Posting: plain ACID transaction; no client-visible conflict model needed. Idempotency keys (client ULID per mutation, unique-indexed) make retried POSTs exactly-once.

## 5. Backend structure (modular monolith)

```
backend/
  src/
    LeafLedger.SharedKernel/          // Money, AccountCode, Period, Ids, Result<T>, errors
    LeafLedger.Modules.Ledger/        // Domain / Application / Infrastructure / Endpoints
    LeafLedger.Modules.ChartOfAccounts/
    LeafLedger.Modules.Subledgers/
    LeafLedger.Modules.Budgets/
    LeafLedger.Modules.Imports/
    LeafLedger.Modules.Documents/     // OCR / QR-bill
    LeafLedger.Modules.Spaces/
    LeafLedger.Modules.Licensing/
    LeafLedger.Modules.Attachments/
    LeafLedger.Modules.MarketData/
    LeafLedger.Host/                  // Program.cs, DI, middleware pipeline, health
  tests/
    LeafLedger.Modules.*.Tests/       // pure domain unit tests
    LeafLedger.IntegrationTests/      // Testcontainers Postgres, API-level
    LeafLedger.FinancialInvariantTests/
```

Rules (enforced by NetArchTest in CI): modules reference only SharedKernel + other modules' public contracts; `*.Domain` references nothing but SharedKernel; EF Core confined to `Infrastructure`. One DbContext per module over a shared database, single migration pipeline.

## 6. API design

```
POST   /api/v1/spaces/{spaceId}/journal-entries          // Idempotency-Key header; 422 structured on invariant violation
POST   /api/v1/spaces/{spaceId}/journal-entries/{id}/reverse
GET    /api/v1/spaces/{spaceId}/journal-entries?filter…  // cursor pagination
GET    /api/v1/spaces/{spaceId}/reports/trial-balance|balance-sheet|income-statement|vat
GET    /api/v1/spaces/{spaceId}/integrity
CRUD   /api/v1/spaces/{spaceId}/accounts|groups|partners|projects|budgets|…
POST   /api/v1/spaces/{spaceId}/imports          // upload → server-side staging session
POST   /api/v1/spaces/{spaceId}/imports/{id}/book
POST   /api/v1/spaces/{spaceId}/documents/extract        // OCR / QR-bill → prefilled entry proposal
CRUD   /api/v1/spaces , /members , /invitations
POST   /api/v1/licenses/activate|validate
POST   /api/v1/spaces/{spaceId}/attachments/upload-grant
GET    /api/v1/marketdata/fx|prices/…
POST   /api/v1/admin/spaces/{spaceId}/purge              // platform-admin, soft, audited
```

- OpenAPI is the **single contract**: TS client + types generated into the frontend on every build; schema-diff gate in CI kills payload drift permanently.
- ProblemDetails everywhere; consistent `/api/v1` prefix (ends the current mixed-prefix mess); rate limiting middleware; pagination, filtering, and error conventions documented once in SharedKernel.

## 7. Frontend structure

```
src/
  api/                 // GENERATED client + types (do not edit)
  application/         // mutations/queries (TanStack Query), view-model hooks,
                       // light pre-validation mirroring server rules (UX only)
  features/            // React pages/components; import application/, never api/ directly? 
                       // (convention: features → application → api)
  shared/              // UI primitives, i18n, formatting contexts
  app/                 // shell, routing, providers, error boundaries
```

- **TanStack Query** replaces the DataApi/Dexie layer: caching, request dedupe, optimistic updates, invalidation keyed by SignalR pings — snappy UX without owning a database.
- Same ESLint boundary enforcement + page-budget ratchet (450 lines / 30 imports), blocking in CI; `JournalEntryPage`-scale files are structurally impossible.
- **One posting flow**: manual entry and import booking both build the same `PostJournalEntry` request; balance/tolerance logic exists once server-side, mirrored once client-side for instant feedback, both pinned by shared golden fixtures.
- PWA: manifest + static shell precache only. Offline = read-only "you're offline" state with last-fetched cache; no queued writes in v1 (revisit only with real user demand — and then as server-drafts, not replication).

## 8. Security model

1. **AuthN:** Entra `common`, audience + signature + issuer-pattern allowlist; MSAL cache in `sessionStorage`; sign-out clears all app state (kills the stale-space auto-join class).
2. **AuthZ pipeline:** scope → license entitlement → space role (typed) → module permission (`ledger.post`, `period.close`, `members.manage`) — endpoint filters, tested per endpoint.
3. **Tenancy:** RLS in Postgres as the second wall behind application checks.
4. **Financial data:** immutable journal + audit triggers = tamper-evident; PITR + daily logical backups; optional hash chain later if compliance demands.
5. **Secrets:** Key Vault references; gitleaks in CI; no destructive scripts in git — per-space purge is an audited admin API.

## 9. Cross-cutting

- **Errors:** Result-based domain errors → ProblemDetails; global exception handler; React error boundary per route + top level.
- **Observability:** OpenTelemetry both sides → Application Insights (SDK in code); correlation id per request flows client → API → SQL; standing alerts: integrity-hash change without posting (P1), 5xx rate, p95 latency, license failures, RLS violations (should be zero, alert on any).
- **Performance budgets:** posting round-trip < 300 ms p95; report views < 500 ms @ 100k lines (indexes + materialized views); bundle size gate in CI. Postgres removes the RU-anxiety class entirely.

## 10. Build & deploy pipeline

- **GitHub Actions**: PR → lint, typecheck, boundaries, budgets, unit + invariant tests, OpenAPI diff, gitleaks, dependency audit. Main → + Testcontainers integration, Playwright smoke → EF migrations against staging → deploy API (slot) + frontend (Vercel) → `/health` + `/integrity` probe → swap; auto-rollback on probe failure.
- Local dev: `docker compose up` (Postgres + API) — one-command onboarding, no Azure needed for feature work.
- Renovate weekly; no beta pins or source patches without ADR + expiry.

## 11. No data migration (fresh start)

**Decision:** no data is migrated from the old Cosmos stack. The new system launches empty.

- Existing users start fresh spaces; where they want history, they self-serve via the old app's **CSV/XLSX exports** → the new app's import pipeline (accounts incl. owners, journal entries, master data — export coverage verified in Phase 0).
- The old stack goes **read-only at sunset start** and remains available for reference/exports through a defined sunset window, then is decommissioned (Cosmos + old App Service + old repos archived).
- Consequences: no delta materializer, no ULID mapping tables, no per-space migration gates or write freezes. Golden fixtures remain — as the **porting oracle** (old vs new behavior), not a data-parity gate.
