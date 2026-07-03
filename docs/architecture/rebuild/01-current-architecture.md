# LeafLedger Rebuild Analysis — Part 1: Current Architecture Reconstruction

> Status: analysis snapshot, 2026-07-03. Source: direct workspace audit of `Accounting/` (frontend, backend, sync, build, docs).
> Companion documents: [02-weaknesses.md](./02-weaknesses.md), [03-target-architecture.md](./03-target-architecture.md), [04-implementation-plan.md](./04-implementation-plan.md), [05-quality-and-maintainability.md](./05-quality-and-maintainability.md).

## 1. System overview

LeafLedger is an **offline-first, multi-tenant, double-entry accounting PWA** for Swiss SME + personal use:

- **Frontend:** React 19 + TypeScript + Vite 8 (beta) PWA. All accounting data lives locally in **IndexedDB (Dexie 4, schema v58, 45 tables)**. All posting logic, validation, FX, VAT, reporting, and subledger logic executes **client-side**.
- **Backend:** ASP.NET Core (net9) API on Azure App Service (Linux, B1). The backend is essentially a **per-tenant delta relay + access control layer**: it stores row-level change deltas in Cosmos DB, enforces space membership, issues blob SAS grants, serves FX/investment prices, and validates licenses. It contains almost **no accounting domain logic**.
- **Storage:** Azure Cosmos DB (serverless, database `leafledger`, 7 containers), Azure Blob Storage (attachments), local OPFS (attachment bytes offline).
- **Realtime:** SignalR hub (`/hubs/sync`) broadcasting `SyncRequired` to space groups.
- **Auth:** Microsoft Entra (MSAL browser, `common` authority — org + personal accounts), JWT bearer on the API.
- **Sibling artifacts:** `Accounting_docs/` (drifted full copy of the app), `docs/` (VitePress site, own nested git repo, own Vercel project), `LeafLedger_Help/` (Docusaurus user manual, Vercel, MSAL-gated middleware).

```
┌───────────────────────────── Browser (PWA) ─────────────────────────────┐
│  React UI (17 features)                                                 │
│    └── view-model hooks + *DataApi.ts   (enforced boundary)             │
│          └── Dexie: AccountingDB v58 (45 tables, spaceId-scoped)        │
│                ├── posting engine (importUtils.bookRows, JournalEntry)  │
│                ├── changeLog (row-snapshot deltas)                      │
│                └── SyncEngine ── CosmosApiSync (HTTP) ── SignalR client │
│  Service worker (Workbox, periodicsync, push)     OPFS attachment bytes │
└───────────────┬──────────────────────────────────────────┬─────────────┘
                │ POST /sync/push, GET /sync/pull           │ SAS upload/download
┌───────────────▼──────────────────────────────┐   ┌────────▼──────────┐
│  LeafLedger.Api (ASP.NET Core, App Service)  │   │ Azure Blob        │
│   Controllers: sync, spaces, members,        │   │ attachments       │
│   license, attachments, fx, investments,     │   └───────────────────┘
│   maintenance(!)                             │
│   LeafLedger.Auth / .Data / .Realtime /      │
│   .Storage / .Core                           │
└───────┬───────────────┬──────────────────────┘
        │               │ SignalR "SyncRequired"
┌───────▼───────────┐   ▼ (single instance, no backplane)
│ Cosmos DB          │
│ deltas   /spaceId  │  spaces /ownerId · memberships /spaceId
│ licenses /partition│  fxRates /baseCurrency · fxRunLogs /runDate
│ investmentPrices   │  /isin
└────────────────────┘
```

## 2. Domain model

Single Dexie database `AccountingDB`, **58 accreted schema versions** declared inline in [src/data/db.ts](../../src/data/db.ts) (3,580 lines). Nearly every table carries `spaceId` and compound `[spaceId+…]` indexes; a `SPACE_SCOPED_TABLES` list drives helpers (`listRowsInActiveSpace`, `requireActiveAccountingSpaceId`).

| Cluster | Tables |
|---|---|
| Core GL | `accounts`, `accountGroups`, `accountOwnerships`, `transactions`, `transactionLines`, `transactionLineAttributions`, `monthlySummaries`, `accountingPeriods`, `periodCloses`, `fxRevaluations`, `fxRates`, `currencies`, `budgetPlans`, `budgetTargets` |
| Master data | `users`, `roles`, `roleRights`, `projects`, `businessPartners`, `businessPartnerAccounts`, `businessPartnerAccountGroups`, `accountingSpaces`, `settings` |
| Import pipeline | `importRules`, `importFlows`, `flowHandles`, `automationRules`, `stagedImports`, `importBatches` |
| Sync bookkeeping | `changeLog`, `syncCursors`, `entitySeqs` (`[spaceId+table+rowId]`) |
| Attachments | `attachments` (bytes in OPFS) |
| Real estate | `realEstateProperties`, `realEstateCosts`, `realEstateMortgages`, `realEstateRentalIncomes`, `realEstateEquityEntries`, `realEstateRentalUnits`, `realEstateMaintenanceRecords`, `realEstateCapitalImprovementRecords` |
| Investments | `investmentPositions`, `investmentLots`, `investmentPriceCache` (global, unsynced), `investmentScheduledAcquisitions`, `investmentManualPrices`, `investmentGlConfigs` (`[spaceId+assetClass+currency]`) |
| VAT | `vatCodes`, `vatPeriods` |

Key domain concepts:

- **Space** = tenant (business/personal × solo/shared); active space persisted in `settings` (`active-space-id`). Feature visibility via `src/shared/featurePolicy.ts` matrix.
- **Account groups**: immutable built-in definitions + custom groups (`src/shared/accountGroups.ts`); defaults (cash-flow activity, FX policy) are **inferred at query time**, not persisted on accounts.
- **Journal entry** = `transactions` header + N `transactionLines` + optional per-line `transactionLineAttributions` (ownership % splits, must sum to 100 ± 0.01).
- **FX**: per-account policy (`fxTreatment`, `fxRateTimingDefault`, `closingRevalue`); each line stores `baseCurrencyAmount` + `fxRateApplied` (IAS 21 audit trail, backfilled v36); period-close revaluation via `periodCloseEngine.ts` → `fxRevaluations`.
- **VAT**: `vatCodes`, nullable VAT fields on lines, `vatPeriods`, report DataApis.
- **Attribution/ownership**: `accountOwnerships` junction feeds attribution derivation for income/expense lines.

## 3. Posting logic (double-entry)

Shared assertions in [src/shared/postingValidity.ts](../../src/shared/postingValidity.ts): `assertPostingAccountsValid`, `assertPostingBusinessPartnersValid`, `assertPostingUsersValid`, `assertPostingProjectsValid`, `assertPostingCurrencyPolicyValid`, `assertPostingPeriodOpen` — throwing structured `PostingValidityError`.

Two posting paths:

1. **Manual** — `JournalEntryPage.tsx` (3,177 lines): balance enforced in UI (base-currency totals for FX entries, tolerance 0.01/0.005); pending entries auto-promote to `cleared` once balanced; persists via `view-model/journalEntryDataApi.ts`.
2. **Import** — `bookRows()` in `importUtils.ts` (2,263 lines): same assertions up front, then books split/full/bank row shapes inside a single Dexie `'rw'` transaction. FX rates must be **prefetched** before the transaction (any non-Dexie `await` inside the Dexie zone causes premature commit — a real production bug class, patched repeatedly). Chunked posting (100 rows/atomic chunk) via `importPostingJob.ts` with a resumable draft.

**Balance is a UI/import-time check only.** Nothing re-validates debits=credits at sync-apply time, server-side, or as a periodic ledger-wide invariant.

## 4. Event-sourcing model (actually: LWW row-snapshot replication)

This is **not event sourcing**. There are no domain events and no diffs. The unit of replication is a **full row snapshot** per `(table, rowId)`:

- **Capture:** Dexie `creating/updating/deleting` hooks on 27 tracked tables write `changeLog` entries `{table, rowId, operation, payload: JSON snapshot, timestamp, synced}` — **deferred outside the data transaction** (drainable thunk queue + `pagehide` flush + `flushPendingChangeLog()`), because the implicit Dexie transaction doesn't include `changeLog`. Subledger tables use explicit `enqueueCreatedRows`/`enqueueDeletedRows`.
- **Push:** `SyncEngine` streams unsynced entries (batches of 1000, timestamp order) → `POST /sync/push` with `{entityType, entityId, op: upsert|delete, payload, baseSeq}`. No idempotency key — a retried push after a lost response appends duplicate deltas.
- **Server:** `DeltaRepository.AppendBatchAsync` persists `DeltaDocument {id: GUID, spaceId, entityType, entityId, op, payload, seq, serverTs, authorId, _type:'delta'}` via per-partition `TransactionalBatch`. **`seq = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + i`** — a wall-clock timestamp, with a "refined later" comment that was never refined.
- **Pull:** `GET /sync/pull?spaceId&cursor` → schema-tolerant query (`c.spaceId OR c.SpaceId`, `c.seq OR c.Seq`, no ORDER BY — sorted in C# because the container lacks a composite index) with three fallback layers including full tolerant scans.
- **Apply:** client sorts pulled entries by a 7-stage FK-dependency `SYNC_ORDER` (users/roles → currencies/vat → projects → group roots → accounts/positions → journal → leaf config), applies each stage in one Dexie transaction with change-tracking suppressed; `table.put(obj)` per row = **idempotent last-write-wins**. `entitySeqs` records the latest server seq per row for optimistic conflict detection (`baseSeq`).
- **Conflicts:** server compares `baseSeq` vs latest seq per entity. Client auto-resolves semantically-equivalent payloads; true divergences reach a modal (Keep mine = clear entitySeq → force push; Accept theirs = apply server payload).
- **Reconstruction:** fresh device replays **the entire delta stream from cursor 0, forever**. No snapshots, no compaction (the `resetRequired` bootstrap path exists in the engine but no adapter ever triggers it).

### Compensating patch layers (accumulated)

The root defect — change capture is not atomic with the data write — has produced at least 11 compensation layers: drainable deferred-write queue; version-gated table-level gap-fill (`GAP_FILL_VERSION` = 4); per-row reconciliation for `PARTIAL_GAP_FILL_TABLES` (grew from 3 journal tables to ~36 tables, effectively full per-row reconciliation every cycle); `filterUnknownToServer` (stop re-pushing server-known rows); sentinel `entitySeqs.seq = 0` markers after push; always-record-seq-on-apply; module-global `activeSyncChain` serialization; cursor-row re-query; semantic-equivalence conflict auto-resolution; space rebind auto-heal guardrails; backend legacy-casing fallbacks + cursor-rewind self-heal + 400→empty-page degradation.

### Consistency rules

A journal entry is **not atomic** across the wire: header, lines, and attributions are independent row deltas. They share stage 6 (same local apply transaction per pulled bundle), but nothing prevents orphan lines if a header delta is lost or lands in a later push batch. No apply-time invariant checks (no balance validation, no FK validation). Deletes do not cascade server-side.

## 5. Cosmos DB structure

| Container | Partition key | Notes |
|---|---|---|
| `deltas` | `/spaceId` | Append-heavy; mixed legacy casing tolerated in queries; no composite index for `(spaceId, seq)`; `EnableScanInQuery=true`; sort in C# |
| `spaces` | `/ownerId` | Point reads by `(spaceId, ownerId)`; cross-partition fallback; non-atomic owner-identity migration (create-then-delete) |
| `memberships` | `/spaceId` | `ARRAY_CONTAINS` candidate matching (email-alias invites); last-admin invariant via COUNT |
| `licenses` | `/partitionKey` (fixed `"license"`) | License + device-binding docs |
| `fxRates` | `/baseCurrency` | Daily hosted service fetch |
| `fxRunLogs` | `/runDate` | Composite-sort 400s → sort in C# |
| `investmentPrices` | `/isin` | AlphaVantage/OpenFIGI-fed |

Containers are created with **default indexing policies** (`CreateContainerIfNotExistsAsync(name, pk)`); the missing composite index for the delta stream is the root cause of the ORDER BY removal and scan fallbacks. Dual-serializer model classes (Newtonsoft attribute + System.Text.Json attribute on every property).

## 6. Module boundaries & API structure

**Frontend layering (genuinely good):** UI/features may only touch data through `view-model/*DataApi.ts`; enforced twice — ESLint `no-restricted-imports` and `scripts/check-architecture-boundaries.cjs` (currently failing on exactly one type-only import: `importUtils.ts` → `data/fxConversion`). Compliance measured at ~99% (87 data imports in features, all but one inside `view-model/`).

**Backend projects:** `Api` (10 controllers, hosted services) → `Data` (repos, `SpaceAccessService`, `EntityValidator`) → `Core` (models/contracts, dependency-free); `Auth`, `Realtime`, `Storage` as side modules. Clean project graph; logic concentration inside `DeltaRepository` (584 lines).

**API surface** (all `[Authorize(Policy="SyncReadWrite")]`): `/sync/push|pull`; `/spaces` CRUD + `/spaces/{id}/members` CRUD; `/api/license/validate|activate`; `/attachments/*` (SAS grants); `/fx-rates`, `/fx-run-logs`; `/api/investments/*`; and `/maintenance/reset-all-data` + `/maintenance/erase-all-data` — **database-wide destruction endpoints gated by the same policy as normal sync**. Route prefix conventions are inconsistent (`/api/...` vs bare).

## 7. UI architecture & state management

- 17 feature folders; no Redux/Zustand/React Query. Pattern: **feature view-model hooks + DataApi modules + a few contexts** (Theme, DateFormat, NumberFormat, Auth/Role).
- Single wildcard route → `AppShell`; `useAppShellComposition.ts` composes ~10 controller hooks (sync progress, conflicts, space switch…).
- Refresh signaling via DOM events (`REMOTE_SYNC_APPLIED_EVENT`, `LOCAL_CHANGE_SYNC_EVENT`).
- i18n: i18next, 5 locales, duplicate-key check wired into `npm run build` (the only enforced guard).
- PWA: Workbox injectManifest SW (`src/sw.ts`), periodicsync → window message, Web Push, update-prompt flow in `main.tsx`; `vite-plugin-pwa` locally patched (patch-package) against a pinned **Vite 8 beta**.
- God-file hotspots: `db.ts` 3,580; `JournalEntryPage.tsx` 3,177; `ImportPreviewTable.tsx` 3,096; `importUtils.ts` 2,263; `ImportStagingPage.tsx` 2,064; `investmentsDataApi.ts` 1,569; `realEstateDataApi.ts` 1,487; `Layout.tsx` 904.

## 8. Cross-cutting concerns

- **Logging:** frontend `createLogger(tag)` with a `registerLogSink()` hook that is **never registered** (console only). Backend: plain `ILogger` console; **no Application Insights SDK, no Serilog** in code (alerts provisioned at infra level via ops script).
- **Error handling:** frontend has a single router-level error boundary; backend has one inline `UseExceptionHandler` returning generic JSON 500 (deliberately before CORS), per-controller `CosmosException` mapping (429/5xx→503, 400→degrade to empty page / skip conflict detection).
- **Security:** JWT audience+signature validated, `ValidateIssuer=false` (multi-tenant `common`); identity-candidate tolerance for stale oids/emails; space access via `SpaceAccessService.EvaluateAccessAsync` (owner or member). MSAL token cache in `localStorage`. Blob keys space-prefixed and normalized against cross-space access. License gating is effectively **client-side only**. Known frontend flaw: stale `active-space-id` + unconditional `upsertMicrosoftUser()` creates local user records in a prior space for a different IDP account (server still protected).
- **Licensing:** in-API `CosmosLicenseService` (hand-rolled HS256 license JWT) + a superseded TypeScript serverless validator in `backend/functions/license` (dead code).

## 9. Build, test, deploy pipeline

- **No CI whatsoever.** No GitHub Actions in any repo. All guards (`check-architecture-boundaries`, `check-page-budget` with ratchet baseline, `check-docs-sync`, `release-safety` incl. live `/health` probe) are manually invoked npm scripts.
- **Frontend deploy:** Vercel Git integration on `main` push (build runs `i18n:keys:check && tsc -b && vite build`).
- **Backend deploy: fully manual** — `dotnet publish` → POSIX-path zip (`tar -a -c -f deploy-posix.zip`) → `az webapp deploy`. `backend/publish/` (139 build-output files) and 11 destructive/ad-hoc `temp-*.py` Cosmos scripts are **committed to git**.
- **Tests:** 175 vitest files (import pipeline ~20, investments ~28, real estate ~18, accounts ~18, sync ~8, financial correctness ~20, dashboards ~12, UI/VM ~30, auth/licensing ~8) + 1 Playwright spec. Backend: 71 xunit attributes, 14 skipped (Cosmos emulator); `SyncController` push/pull core logic and the 584-line `DeltaRepository` are effectively **untested**. **No ledger-wide trial-balance invariant test exists.**

## 10. Dependencies & external services

React 19.2, react-router 7.13, Dexie 4.3, MSAL browser 5.4, SignalR 10, i18next 25.8, recharts 3.7, pdfjs-dist 5.5, tesseract.js 7 (OCR groundwork), xlsx 0.18.5 (known CVEs in that version line), Vite 8 beta (pinned + patched plugin), vitest 4.1, Playwright 1.56. Backend: net9, Cosmos SDK 3.47, Blobs 12.23, JwtBearer 9.0.4, Newtonsoft 13. External: Microsoft Entra, Azure App Service B1, Cosmos serverless, Blob Storage, Vercel ×3 (app, docs, help), AlphaVantage + OpenFIGI, exchange-rate source for FX hosted service.

## 11. Coding conventions observed

- TypeScript strict, flat ESLint, view-model boundary discipline, space-scoped Dexie helpers, structured `PostingValidityError`s, translated i18n keys with role-based test queries — all consistently applied.
- Countervailing Vibe-Coding signatures: god files, 58 inline schema migrations, copy-paste dual-app (`Accounting_docs`), defensive try/catch layering instead of root-cause fixes, comment-driven workaround archaeology ("refined later", "Do NOT use ORDER BY"), committed temp scripts and artifacts, magic strings for roles/ops/types.
