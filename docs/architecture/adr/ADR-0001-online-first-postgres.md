# ADR-0001: Online-first architecture + PostgreSQL as system of record (fresh-start launch)

- **Status:** accepted
- **Date:** 2026-07-03 *(authored 2026-07-06)*
- **Deciders:** Project owner + LL Architect (rebuild direction, 2026-07-03)
- **Related:** WP P1-WP05 (this record); target architecture Part 3 §"Decision (2026-07-03)", §1–§2; Part 4 §"Phase 1"/§"Phase 7"; weaknesses Part 2 §"client is the source of truth"; supersedes the earlier offline-first commit-model draft

## Context
The old system (`Lokkeccs/Accounting`) is an **offline-first PWA**: Dexie/IndexedDB is the client-side
source of truth (schema v58, 45 tables, 58 migrations), changes replicate to Azure Cosmos DB through a
hand-rolled sync subsystem (SyncEngine, changeLog, entitySeqs, gap-fill, last-writer-wins conflict
resolution). This architecture is the root of the system's most serious problems (Part 2):

- **The client is the source of truth**, so the server validates almost nothing about payloads — a
  financial-integrity hazard for a double-entry ledger, and a security hole (cross-tenant writes,
  N sequential Cosmos round-trips per push).
- **Numeric local Dexie ids used as global entity ids** collide across offline devices; composite-key
  tables need hashed pseudo-ids to fit the model.
- The **replication problem class** (wall-clock sequencing, LWW merges, gap-fill, conflict UX) is an
  11-patch-layer "sync onion" that cannot be made correct incrementally.
- **Cosmos DB** cannot enforce the invariants a ledger needs (no cross-document ACID, no FK/CHECK
  constraints, weak ad-hoc reporting).

A foundational direction had to be set before any new schema, entity, or endpoint could exist: *does the
rebuild keep offline-first writes, and what is the store of record?*

## Decision
We will rebuild LeafLedger as an **online-first application with the server as the single source of
truth**, on **PostgreSQL**, launching as a **fresh start with no automated data migration**. Concretely:

1. **Server owns all state.** The client becomes a thin, cache-aware React SPA (TanStack Query cache +
   server truth). The PWA keeps only a manifest + static-shell precache — **no data sync, no
   periodic-sync, no OPFS byte store**.
2. **No offline writes, therefore no client-side data store or schema-migrations.** Dexie/IndexedDB and
   the entire sync subsystem are dropped. The "58 Dexie migrations" problem class is **deleted, not
   solved**. Light client-side pre-validation is retained for instant UX only, never as authority.
3. **PostgreSQL (Flexible Server; EF Core + Npgsql; row-level security per space)** replaces Cosmos as
   the store of record — chosen for ACID across entry + lines + attributions, FK/CHECK constraints
   (including a database-enforced balance trigger and immutable-journal + reversal model), first-class
   SQL reporting, point-in-time-restore backups, and materially lower cost at this scale.
4. **Fresh-start launch, no bulk data migration.** The new system launches empty; existing users
   **self-migrate** via CSV/XLSX export from the old app → import into the new one, while the old stack
   runs **read-only** (posting disabled, exports enabled) during a sunset window, then is decommissioned.
   The *execution* of this rollout is Phase 7; this ADR records the *decision* that there is no
   automated old→new data-migration pipeline.

This supersedes the earlier hybrid / offline-first commit-model draft.

## Consequences
- **Positive:** The replication problem class disappears entirely — non-atomic change capture,
  wall-clock sequencing, LWW merges, gap-fill, and conflict UX cease to exist as categories.
- **Positive:** Financial integrity becomes enforceable *by construction* — the server validates every
  write and the database enforces ledger invariants (balance, FKs, RLS, immutability).
- **Positive:** Radical simplification — deleting offline sync removes the single largest source of
  complexity and bugs, enabling the ten-year-maintainability goal.
- **Positive (launch):** No risky, expensive bulk ETL; users bring only the data they need; the import
  pipeline doubles as a real-world tested onboarding path.
- **Negative:** **Offline writes are no longer supported.** Users need connectivity to record entries.
  This is the one assumption to validate with usage data before full commit (Part 4, risk #4).
- **Negative (launch):** Self-migration puts effort on users and depends on old-app export coverage
  (closed in Phase 0); there is no automatic historical-data carryover.
- **Neutral:** Backend schema evolution now uses server-side **EF Core migrations** (Phase 2+); this is a
  different, smaller concern than the dropped *client-side* migrations and is out of scope here.

## Alternatives considered
- **Keep offline-first writes + Cosmos (patch the sync onion).** Rejected: the replication model is the
  root cause of the integrity, security, and complexity problems; it cannot be made correct
  incrementally, and Cosmos cannot enforce ledger invariants.
- **Online-first but stay on Cosmos DB.** Rejected: no cross-document ACID, no FK/CHECK constraints,
  weak SQL reporting — exactly what a double-entry ledger needs most.
- **Hybrid (server-authoritative with a constrained offline cache for writes).** Rejected for launch:
  reintroduces a smaller version of the replication problem class for a benefit not yet shown to be
  required; revisit only if usage data proves offline writes are needed.
- **Automated bulk data migration (Cosmos → Postgres ETL) at launch.** Rejected: high cost/risk to
  faithfully move a fundamentally different data model; self-migration via export/import is simpler,
  safer, and validates the import pipeline.
