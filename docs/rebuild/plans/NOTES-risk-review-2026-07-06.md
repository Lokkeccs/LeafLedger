# Plan notes — architecture risk review (2026-07-06)

> Binding planning notes from the external architecture risk review, triaged and approved by the user on 2026-07-06.
> **LL Architect must fold each item below into the named WP plan when that plan is drafted.** Each item carries the acceptance-criteria wording to copy in. Items not listed here were assessed as already covered by the specs or explicitly rejected (see "Assessed, not adopted" at the bottom).

## Adopted items

### N1 — ID storage format decision → **P1-WP03 (SharedKernel: Money, Ids, Period, Result)**
ULIDs are 128-bit; the spec says "prefixed ULIDs stored as `text`/`uuid`-compatible" (Part 3 §"IDs") without deciding the column type. Decide **in P1-WP03, before any schema exists**:
- **Recommended:** store as `uuid` (native 16-byte, fast indexes, no collation issues); the type prefix (`je_`, `acc_`, …) is applied/stripped at the API boundary only, never stored.
- Fallback if uuid proves awkward: `text` with `COLLATE "C"` and a length check.
- Record the decision as an ADR (fits P1-WP05's ADR log).

**Acceptance criteria to copy into P1-WP03:**
- SharedKernel `Id<T>` (or equivalent) round-trips ULID ↔ uuid losslessly, preserving lexicographic sort order (property test).
- Prefix handling is API-boundary-only; unit test proves the storage representation contains no prefix.
- ADR drafted stating column type, prefix policy, and rationale.

### N2 — Idempotency key lifecycle → **Phase 2 ledger-core WP (idempotency middleware)**
Spec defines the mechanism (client ULID per mutation, unique-indexed) but no lifecycle.
- Keys stored with a **24 h TTL**; background cleanup job (or `pg_cron`/scheduled delete) purges expired keys.
- **Collision counter metric** (same key, different payload hash → reject + increment) wired into observability; standing alert on anomalous rate.

**Acceptance criteria to copy in:**
- Integration test: retried POST with same key + same payload → 200/201 replay, no double posting.
- Integration test: same key + different payload → 409/422, collision metric incremented.
- Expired key (TTL elapsed) behaves as a fresh request; cleanup job covered by a test or migration-verified schedule.

### N3 — Eager balance check in posting service → **Phase 2 posting WP**
The deferred DB constraint trigger (runs at COMMIT) remains the last wall. Add a **non-deferred application-level check** in the posting service for developer feedback and diagnosability:
- Posting service validates debits ≡ credits per entry *before* the transaction commits and returns structured 422 on violation.
- If the DB trigger ever fires despite the eager check (should be impossible), log at Error with the **full entry payload** and correlation id.

**Acceptance criteria to copy in:**
- Unit test: unbalanced entry rejected by the service with 422 + structured problem details (never reaches the DB).
- Integration test: trigger still rejects a crafted direct-SQL unbalanced insert (both walls verified independently).

### N4 — Materialized-view refresh strategy → **Phase 2/4 reporting WP**
Spec says views are "materialized where hot, refreshed on posting" without a mechanism. When the reporting WP is planned:
- Use `REFRESH MATERIALIZED VIEW CONCURRENTLY` (requires unique index on the view).
- Post-commit **enqueue** of refresh jobs (never inline in the posting transaction); coalesce multiple postings into one refresh.
- **Refresh-duration metric** + staleness indicator surfaced to observability.

**Acceptance criteria to copy in:**
- Posting latency test proves refresh is off the posting critical path.
- Concurrent posting + refresh test shows no lock contention failures.
- Metric emitted per refresh with duration and rows affected.

### N5 — SignalR invalidation coalescing → **Phase 3 app-shell WP**
Prevent thundering-herd refetches when many users share a space:
- **Server-side coalescing:** at most one invalidation ping per space/topic per ~200 ms window.
- **Client-side batching:** TanStack Query invalidations debounced/batched per convention (single utility, not per-page ad hoc).
- Postgres NOTIFY/LISTEN was assessed and **rejected as premature** — do not implement.

**Acceptance criteria to copy in:**
- Server test: N rapid postings in one space produce ≤ ceil(window-count) pings, not N.
- Client test: burst of pings triggers one refetch cycle per query key.

### N6 — TanStack Query invalidation map → **Phase 3 conventions WP**
Make the **invalidation map per module** an explicit, reviewable deliverable (which mutations invalidate which query keys), not folklore:
- One documented map (module → mutation → query keys) checked into `app/` docs.
- Cache-correctness integration tests: after each mutation type, the mapped keys refetch and unmapped keys do not.

**Acceptance criteria to copy in:**
- Invalidation map document exists and covers every mutation in the WP's scope.
- At least one cache-correctness test per module proving mapped-key refetch + unmapped-key stability.

## Deferred (revisit when the trigger condition occurs)
| Item | Trigger to revisit |
|---|---|
| CI fast-path (path-filtered lanes) + migration dry-run step | When Playwright/Testcontainers/migrations make the pipeline slow (Phase 2–3) |
| Tablet-mode breakpoint | Real user demand; until then only "no desktop-only layout primitives" |
| Second OCR adapter (Tesseract/Google Vision) | Azure Document Intelligence pricing/API change; the port abstraction suffices for now |

## Assessed, not adopted
- Module-boundary automation, RLS tests/alerts, idempotency mechanism, single migration pipeline, OCR port, monolith discipline, no-offline-writes → **already covered** by Parts 3/5 and P1-WP01.
- Postgres NOTIFY/LISTEN → SignalR bridge, `bytea(16)` IDs → **rejected** (premature / micro-optimization).
