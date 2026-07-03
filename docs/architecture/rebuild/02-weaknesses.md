# LeafLedger Rebuild Analysis — Part 2: Architectural Weaknesses & Problems

> Brutally honest critique, ranked by **risk to financial integrity** first, then maintainability. Evidence references are in [01-current-architecture.md](./01-current-architecture.md).

## Severity legend

- **S1** — can silently corrupt or lose financial data
- **S2** — can expose/destroy data or block operation
- **S3** — structural debt that compounds cost and risk
- **S4** — hygiene/quality issues

---

## 1. Financial-integrity risks (S1)

### 1.1 Change capture is not atomic with the data write — the root defect
Dexie hooks write `changeLog` deltas **after** the data transaction commits (deferred thunk queue). Every one of the historical data-loss incidents traces to this: lost owner deltas, doubled ownerships (dropped DELETE delta), vanished investments/real-estate rows after cache clear, duplicate Cosmos upserts. At least **11 compensation layers** (gap-fill versions, `PARTIAL_GAP_FILL_TABLES` grown to ~36 tables, sentinel seqs, `filterUnknownToServer`, global sync chain) patch symptoms; none removes the race. In an accounting system, "the ledger row exists but its replication record may not" is an unacceptable base invariant.

### 1.2 No atomic journal-entry unit of replication
A journal entry (header + lines + attributions) is pushed and pulled as **independent row deltas**. A lost or reordered header delta yields orphan lines stored silently (apply is blind `put`, no FK or invariant checks). Deletes don't cascade. The very existence of `journalSyncDiagnostics.ts` proves headers and lines go missing independently in production.

### 1.3 Balance (debits = credits) is never re-verified after entry time
Balance is checked only in the UI/import path at posting time, with two different tolerances (0.01 vs 0.005). It is not validated: server-side at push, at sync-apply, at period close as a ledger-wide assertion, or by any test ("no system-wide trial-balance test" — confirmed). Any sync bug, partial chunk commit, or LWW overwrite can leave an unbalanced ledger **undetected indefinitely**.

### 1.4 `seq` is a wall-clock timestamp
`Seq = UtcNow.ToUnixTimeMilliseconds() + i` with a "refined later" comment. Two concurrent pushes in the same millisecond can interleave/duplicate seqs; clock skew or NTP adjustment on the App Service host can produce non-monotonic sequences; a client whose cursor advanced past a slightly-earlier concurrent write **permanently misses deltas**. The total order of the financial event stream — the one thing an event store must guarantee — rests on server clock behavior.

### 1.5 Push is not idempotent
No client-generated delta ID / idempotency key. A retried push after a lost HTTP response **appends duplicate delta documents**. Convergence relies solely on apply-side LWW `put`. Evidence that this happens: `temp-cosmos-dedupe-deltas.py` exists because duplicates had to be manually purged from production.

### 1.6 LWW at whole-row granularity for financial records
Concurrent edits to the same transaction from two devices silently drop one side's fields. Conflict detection only fires when `baseSeq` is known and stale — sentinel rows (`baseSeq: null`) push **unconditionally**, bypassing conflict detection entirely. "Keep mine" force-wins with no audit trail.

### 1.7 Dexie transaction-zone fragility in the posting path
Any non-Dexie `await` inside `bookRows`'s transaction silently commits early, leaving a chunk **half-booked** (documented incident: `PrematureCommitError`, three separate culprits fixed). Atomicity of posting depends on developers never awaiting the wrong promise inside a 2,263-line file — a convention, not a guarantee.

### 1.8 Availability-over-consistency degradations
Backend maps Cosmos 400 to "pull returns empty page" and "push skips conflict detection/dedupe". A misconfigured index can therefore make the API **pretend there is no data** or accept writes without conflict checks — exactly the wrong trade for a ledger.

### 1.9 Derived balances can drift
`monthlySummaries` is local-only, rebuilt by self-heal heuristics ("rebuild when empty and transactions exist"). There is no checksum between derived summaries and the transaction base, so dashboards can disagree with the GL.

## 2. Security concerns (S2)

1. **`/maintenance/reset-all-data` and `/maintenance/erase-all-data` are reachable by any authenticated sync user.** Reset **drops the entire Cosmos database across all tenants**. No admin role, no environment gate, no confirmation token. This is the single worst finding in the codebase.
2. **License enforcement is client-side only** — the API's `SyncReadWrite` policy checks the Entra scope, not the license. Any registered user can bypass plan limits by calling the API directly.
3. **Role model is magic strings** (`"admin"` case-insensitive compare) with owner implicit; no permission granularity server-side; frontend `roles`/`roleRights` tables are not enforced by the API at all.
4. **MSAL token cache in `localStorage`** (persistent, XSS-readable).
5. **Stale-identity auto-join** (documented): a different IDP account logging in on a shared device gets a local user record in the previous space.
6. `ValidateIssuer = false` — defensible for `common` multi-tenant, but combined with hardcoded audience and no issuer allowlist it broadens token acceptance.
7. Committed ops scripts with destructive capability (`temp-cosmos-purge-all-data*.py`) and `backend/publish/` output in git.

## 3. Event-sourcing / sync design flaws (S1–S2)

1. **It isn't event sourcing.** Full-row snapshots ("account is now this") not domain events ("JournalEntryPosted"). No intent, no invariants, no audit semantics, no ability to project new read models from history.
2. **Unbounded stream, full replay forever.** First sync cost grows linearly with total edit history; duplicate upserts bloat it further. No snapshots/compaction (bootstrap path exists but is unreachable dead code).
3. **The client is the source of truth** for a multi-device system: server validates nothing about payloads beyond `EntityValidator` shape checks (N sequential Cosmos round-trips per push — also a performance bug).
4. **Numeric local Dexie ids as global entity ids.** `rowId` is the auto-increment IndexedDB key, parsed with `Number.parseInt` server-side (NaN skipped = silent drop). Two devices creating rows offline can collide; composite-key tables need hashed pseudo-ids (`investmentGlConfigs`) to fit the model.
5. **`_suppressChangeLog` is a module-level boolean** — not re-entrant, safe only because of the global sync serialization that exists to patch another bug.
6. **Client/server contract drift tolerated forever**: `spaceId OR SpaceId`, `seq OR Seq`, `payload OR payloadJson` in every query instead of a one-time backfill migration.
7. Stage-7 "no enforced FK, order non-critical" is a stated non-guarantee of referential integrity.
8. SignalR notifier has **no backplane** and an in-memory membership tracker — silently breaks on scale-out to 2 instances.

## 4. Cosmos DB problems (S2–S3)

1. **No indexing policy management at all** — default policies; the missing `(spaceId, seq)` composite index caused the "Cosmos silently returns 0 rows on ORDER BY" trauma, answered by removing ORDER BY, sorting in C#, `EnableScanInQuery=true`, and 2,000–5,000-doc fallback scans. RU cost per pull is unbounded on the legacy paths.
2. `MAX(seq)` scans per pull; per-entry conflict checks chunked into OR-clause queries (SC1030 workarounds) — O(pushed entities) RU per push.
3. `spaces` partitioned by `/ownerId` forces cross-partition reads for "find space by id" and makes owner migration a **non-atomic create-then-delete across partitions**.
4. `licenses` uses a single fixed partition value — a deliberate hot partition.
5. Dual-serializer attributes on every model property (Newtonsoft + System.Text.Json) — a standing trap when one attribute is forgotten.
6. Schema is `payload` as a **JSON string inside JSON** — double-encoded, unqueryable server-side, no server-side projection possible.

## 5. Clean-architecture / SOLID / DDD violations (S3)

1. **No domain layer anywhere.** Frontend: business rules live in view-model DataApis and 3,000-line pages. Backend: repositories + controllers, zero domain model. `LeafLedger.Core` is DTOs, not a domain. The ubiquitous language (journal, posting, period, subledger) exists only in file names.
2. **SRP violations at scale**: `db.ts` (3,580 lines = schema + 58 migrations + entities + tenancy helpers + change tracking + active-space state), `JournalEntryPage.tsx` (3,177 = form state + FX math + validation orchestration + rendering), `DeltaRepository` (584 = query building + 3 fallback strategies + mapping + dedupe).
3. **DIP**: features depend on concrete Dexie-shaped types re-exported through DataApis; posting logic depends on concrete `db` module; no ports/interfaces for storage, clock, FX source.
4. **OCP**: adding a synced table requires touching `trackedTables`, `SYNC_TABLES`, `SYNC_ORDER`, `KNOWN_SYNC_TABLES`, `PARTIAL_GAP_FILL_TABLES`, `GAP_FILL_VERSION`, backend `EntityValidator` — six shotgun-surgery sites; history shows each new table shipped with a data-loss bug first.
5. **Aggregate boundaries ignored**: the JournalEntry aggregate is split into three independently-replicated tables; account+ownership likewise. DDD would demand the aggregate be the consistency unit.
6. **Two posting pipelines** (manual page vs importUtils) duplicate balance/FX/attribution logic with divergent tolerances.
7. **Anemic API**: backend controllers accept any payload for any entityType — the server cannot distinguish a valid journal entry from garbage.

## 6. Vibe-coding auto-generation smells (S3–S4)

- **Whole-app duplication** (`Accounting_docs/`) requiring double-fixes, with confirmed drift (`importPostingJob.ts` missing in the copy; one copy had guaranteed premature-commit FX code).
- Layered defensive patches instead of root-cause fixes (the 11-layer sync onion; try/catch around cache reads "to survive schema mismatch").
- Archaeological comments as institutional memory ("refined later", "Do NOT use ORDER BY…").
- 11 committed `temp-*.py` scripts (some destructive, one with personal OneDrive paths), committed `publish/` output, committed regenerated `artifacts/*.json`, nested git repo inside `docs/`.
- Magic strings everywhere (roles, ops, `_type`, `"license"` partition, hardcoded client ID, CORS origins in code *and* config).
- Dead code: unreachable snapshot bootstrap, superseded TS license function, unwired `ActiveSpaceProvider`, unused `registerLogSink`.
- Stale docs (sync-engine.md stage table missing ~10 tables).

## 7. Performance bottlenecks (S3)

1. Pre-push per-row reconciliation across ~36 tables **every sync cycle** — O(rows) IndexedDB scans on every sync, growing with ledger size.
2. Full delta replay on every fresh device / cache clear (unbounded; already required manual dedupe in production).
3. `EntityValidator` = N sequential Cosmos reads per pushed batch.
4. Cosmos fallback scans (2,000–5,000 docs) + per-pull `MAX(seq)`.
5. 3,000-line React pages re-rendering on form state; no virtualization noted for large tables beyond DataTable tweaks.
6. Single B1 App Service instance is a hard ceiling (SignalR tracker breaks on scale-out anyway).

## 8. Testing gaps (S2 for a financial system)

1. **No ledger-wide trial-balance/accounting-equation invariant test.**
2. **No property-based or golden-master tests** for posting, FX rounding, VAT computation.
3. Backend's riskiest code — `SyncController` push/pull, `DeltaRepository` (all fallbacks), owner migration, JWT pipeline, attachments — effectively untested (14 tests permanently skipped on emulator dependency).
4. One Playwright spec total; no multi-device sync simulation harness despite sync being the #1 defect source.
5. Frontend tests mock `db.ts`/DataApis wholesale — `enqueueCreatedRows` internals (a proven bug site) are untested; adding a DataApi export breaks two mock files (test brittleness by design).
6. No coverage measurement configured at all.

## 9. Build/deploy pipeline issues (S2–S3)

1. **No CI.** Boundary check currently *fails* on main and nobody is forced to notice. Docs-sync, page budgets, release safety — all honor-system.
2. Backend deploy is a manual multi-step ritual with a known Windows-zip landmine (backslash paths → EINVAL on Linux) — institutional knowledge, not automation.
3. Frontend ships on every push to `main` via Vercel with **no test gate in front of it** (only the i18n key check inside `npm run build`).
4. Vite pinned to a **beta** + patch-package on the PWA plugin = fragile upgrade path.
5. `xlsx@0.18.5` has known CVEs; no dependency audit in any pipeline.

## 10. Maintainability summary

The codebase is *better disciplined than typical AI-generated output* (real layering, 175 test files, enforced import boundaries, meaningful docs). But its core replication design is structurally unsound for accounting: **non-atomic capture, non-idempotent push, wall-clock ordering, no aggregate atomicity, no server-side validation, no invariant re-verification** — each individually patched, collectively unfixable without redesign. The honest verdict: the **UI layer and domain rules are salvageable; the sync/event layer and backend data layer must be rebuilt**.
