# P2-WP09 — Idempotency middleware (N2)

- **Phase:** 2 (ledger core)
- **State:** verify. Unblocks the WP08 exactly-once slice (**I5**) after Docker-backed acceptance tests pass.
- **Owner (implementation):** LL Backend Dev
- **Depends on:** P2-WP02 (schema + `leafledger_app` role + `app.current_space_id` RLS pattern + `postgres:17` Docker fixture), P2-WP05 (the two write endpoints + `JournalPostingService.ExecuteAsync` transaction + the already-threaded `IdempotencyKey` field + the `Idempotency-Key` header reader), P2-WP06 (principal-bound txn-local RLS binding this WP runs inside).
- **Blocks:** **P2-WP08** (invariant **I5** "retried posts exactly-once"), production exposure of the WP05 write endpoints (they are explicitly *not* production-ready until WP06 + WP09 land — recorded in the WP05 plan).
- **Estimated size:** ≤ 2 days (one raw-SQL EF migration for `idempotency_keys` + the enforce/store/replay logic inside the existing posting transaction + a background TTL cleanup + a collision metric + header validation + integration tests + contract regen). At the size limit — see [Risks](#risks--notes).

## Re-scope lineage (LL Architect)

WP09 was **carved out of the original P2-WP05 bundle** on 2026-07-11 (recorded in status.md and the WP05 plan) because *posting + reversal + period lifecycle + idempotency (N2) + eager balance (N3)* exceeded the ≤ 2-day rule. WP05 shipped the two write endpoints and **reserved** the `Idempotency-Key` header contract (the endpoint parses it into the command; `JournalPostingService.ExecuteAsync` accepts an `idempotencyKey` parameter) but implemented **no** storage/replay — that is this WP. WP09 fills exactly that reserved seam; it does not re-open posting/reversal behaviour.

## Spec sources

- `docs/architecture/rebuild/03-target-architecture.md` §"Posting" (verbatim): "*Idempotency keys (client ULID per mutation, unique-indexed) make retried POSTs **exactly-once**.*"; §6 API surface: `POST /api/v1/spaces/{spaceId}/journal-entries` carries an **`Idempotency-Key` header**; §22 ("HTTPS (JWT bearer, **idempotency keys on writes**)").
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 2 ("idempotency middleware" is a named Phase-2 deliverable; exit criterion includes "**retried posts exactly-once**").
- `docs/architecture/rebuild/05-quality-and-maintainability.md` §1.1 (invariant row: "idempotent retries exactly-once"), §67 ("Correlation: request/**idempotency ULID** as trace id from client mutation → API → SQL → SignalR ping").
- `docs/architecture/rebuild/07-vibe-coding-playbook.md` §88 ("Every endpoint: **idempotency key on writes**, ProblemDetails errors, authorization filter, and an integration test against Testcontainers Postgres"), §204 (non-negotiable #4: "**Every write endpoint is idempotent (idempotency key)** and authorized").
- **N2** (the binding risk-review item) — `docs/rebuild/plans/NOTES-risk-review-2026-07-06.md`: keys stored with a **24 h TTL** + background cleanup; **collision counter metric** (same key, different payload hash → reject + increment); the three acceptance criteria are copied verbatim into [Acceptance criteria](#acceptance-criteria-concrete-tests).
- `docs/architecture/rebuild/02-weaknesses.md` §1.5 (the OLD push was **non-idempotent** — a retried push appended duplicate deltas, requiring a manual `temp-cosmos-dedupe-deltas.py`; the *evidence* that motivates this WP, not a design to port).
- ADR-0001 (server + DB are the source of truth), ADR-0002 (uuid ids — the ULID↔uuid storage convention reused for the key column).

## Goal

Make the two WP05 write endpoints **exactly-once** under client retries, by persisting a client-supplied idempotency key **atomically with the write** and replaying the original response on a retry — so a lost-response retry never double-posts:

- **Retry, same key + identical payload** → the original response is **replayed** (same entry id / `entry_no`, no second row), with an `Idempotent-Replayed: true` response header.
- **Same key + different payload** → **409** `idempotency.key_reused` (a collision), and a `leafledger.idempotency.collisions` counter is incremented.
- **Expired key** (row older than the 24 h TTL) → treated as a **fresh** request.
- **Missing / malformed key** on a write → **400** (`idempotency.key_required` / `idempotency.key_invalid`) — the key is **required** on writes per non-negotiable #4.
- **Concurrent duplicate** (two in-flight requests, same key) → exactly one posts; the second blocks on the uniquely-indexed row and then replays.

The key row is space-scoped and RLS-protected exactly like every other tenant table, so a key from space A can never replay into space B.

## Scope

1. **`idempotency_keys` table (new raw-SQL EF migration `IdempotencyKeys`).**
   - Columns: `space_id uuid NOT NULL`, `idempotency_key uuid NOT NULL` (the client ULID stored as uuid via the SharedKernel N1 converter — see D-WP09-KEY), `actor_id uuid NOT NULL`, `target text NOT NULL` (the logical operation, e.g. `post` / `reverse:{entryId}`, so a key is scoped to one operation), `request_hash bytea NOT NULL` (SHA-256 of the canonical request — see D-WP09-HASH), `response_status int NOT NULL`, `response_body jsonb NOT NULL`, `created_at timestamptz NOT NULL DEFAULT now()`.
   - **`UNIQUE (space_id, idempotency_key)`** — the exactly-once anchor.
   - **RLS**: `ENABLE` + `FORCE ROW LEVEL SECURITY` + a `p_idempotency_keys_isolation` policy on `space_id = current_setting('app.current_space_id', true)::uuid` — added to the WP02 tenant-table pattern (the same `USING`/`WITH CHECK` shape as `journal_entries` et al.).
   - **Grants**: `GRANT SELECT, INSERT ON idempotency_keys TO leafledger_app`. **No UPDATE** (a key row is written once, like the append-only journal). Cleanup runs on the base (owner) connection — see D-WP09-CLEANUP — so `leafledger_app` needs no DELETE grant.
   - Written via `migrationBuilder.Sql(...)` (raw SQL, **not** `CreateTable`) and the table is **not** mapped as an EF entity, so `HasPendingModelChanges()` stays green (the WP07 raw-SQL precedent). `Down` drops the policy + table.
2. **Enforcement inside the existing posting transaction (`JournalPostingService`).**
   - `ExecuteAsync` currently receives `idempotencyKey` and ignores it. WP09 makes it: (a) look up `(space_id, key)` within the bound transaction; (b) if a **live** row exists and `request_hash` matches → short-circuit and return the stored `response_status` + `response_body` as a **replay** (roll back the empty transaction; no new entry); (c) if a live row exists and the hash **differs** → return a **409 collision** + increment the metric; (d) if the row is **expired** (`created_at < now() - 24h`) → treat as absent (proceed); (e) otherwise run the operation and, **in the same transaction**, `INSERT` the key row with the produced status + body, so key + entry commit atomically.
   - **Concurrency:** the `INSERT` on `(space_id, key)` is inside the transaction; a concurrent duplicate blocks on the unique index until the first transaction commits, then finds the committed row and replays. On a unique-violation race, the service rolls back and re-reads → replay. No double `entry_no`.
3. **Required header + validation (`LedgerEndpoints`).**
   - The `Idempotency-Key` header is **required** on both write endpoints: missing → **400** `idempotency.key_required`; malformed (not a valid ULID) → **400** `idempotency.key_invalid`. On replay, the endpoint returns the stored status + body verbatim and sets `Idempotent-Replayed: true`. (Reads/reports are unaffected.)
4. **TTL cleanup (background).**
   - A lightweight hosted background service (`IdempotencyCleanupService`) periodically `DELETE FROM idempotency_keys WHERE created_at < now() - interval '24 hours'` on the **base (owner) connection** (not the `leafledger_app` role), so it purges across all tenants (RLS would otherwise fail-closed with no space context). Interval is a bounded constant (documented); a single pass is directly testable.
5. **Collision metric.**
   - A `System.Diagnostics.Metrics.Counter<long>` named `leafledger.idempotency.collisions` (BCL, no new NuGet) incremented on every same-key/different-payload rejection, tagged with `space_id`. Wired through DI so a test `MeterListener` can assert the increment.
6. **Contract + tests.** Regenerate `backend/openapi/leafledger-v1.json` + the TS client (the `Idempotency-Key` header becomes **required**; add the **409** collision response + the **400** validation responses to the two write operations); the CI `contract` diff gate must stay green. Add idempotency integration tests + a request-hash determinism unit test + a cleanup test; update the WP05 HTTP tests to send a key (test-only).

## Non-goals (explicitly deferred)

- **No change to posting/reversal accounting behaviour.** WP04 domain and WP05 orchestration/validation are untouched; WP09 only wraps them with the key check + replay. The eager balance check (N3) and both balance walls stay exactly as WP05 shipped them.
- **No general-purpose HTTP idempotency middleware for arbitrary endpoints.** Enforcement is scoped to the two ledger write endpoints (the only Phase-2 writes). "Middleware" in the spec is honoured by an equivalent, but **transaction-atomic**, in-service mechanism (see D-WP09-LAYER); a generic pipeline component that cannot be atomic with the DB write is explicitly rejected. A shared abstraction for future write endpoints is a carry-forward, not this WP.
- **No observability platform / alerting.** The collision **counter** is emitted (N2); wiring it to a dashboard + standing alert is an ops/observability task (Phase 2/4 observability WP), recorded as a carry-forward.
- **No `pg_cron` / DB-scheduled job.** Cleanup is an app-side hosted service (N2 allows "background cleanup job *or* `pg_cron`/scheduled delete"); `pg_cron` is not assumed present on `postgres:17`.
- **No authentication change.** Reuses WP06's principal-bound binding as-is; real Entra is P2-WP11.
- **No frontend beyond regenerated `api/**`.** The client already sends the header (WP05 reserved it); no React change.
- **No idempotency for period-lifecycle writes.** WP10's endpoints will opt into the same seam when they exist (a WP10 note); WP09 wires only the two WP05 endpoints.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

### Reference only (not salvaged) — the anti-pattern this WP fixes
- The OLD sync push had **no idempotency key** (weaknesses §1.5): a retried push after a lost response appended duplicate delta documents, and production duplicates had to be manually purged (`temp-cosmos-dedupe-deltas.py`). This is the concrete failure mode WP09 eliminates — it is **evidence/motivation, not code to port**. A GitHub search of the OLD repo for any server-side idempotency-key store or replay path returns none.

### Rewrite (spec-derived; no OLD oracle)
- The `idempotency_keys` table, the atomic enforce/store/replay, the TTL cleanup, and the collision metric are greenfield per §"Posting" + N2. They are pinned by **integration tests + the WP08 I5 property**, not by golden fixtures (there is no OLD behaviour to match).

## Accounting decisions

**None.** Idempotency is a mechanical transport/persistence concern (exactly-once delivery), not an accounting rule. No LL Accounting Expert consult is required. (The *money* invariants it protects — no double-post — are already pinned by WP04/WP05/WP08.)

## Decisions

- **D-WP09-REQ — the key is required on writes.** Per non-negotiable #4 ("every write endpoint is idempotent (idempotency key)") the header is **mandatory** on both write endpoints; missing/malformed → 400. This tightens the WP05-reserved contract (where the header field existed but was optional/unused) and updates the WP05 HTTP tests to send a key. The endpoints are not production-exposed yet, so the tightening is safe.
- **D-WP09-KEY — store the client ULID as `uuid`.** The key is a client-supplied ULID (§"client ULID per mutation"); validate its format and store it as `uuid` via the existing SharedKernel N1 ULID↔uuid converter (ADR-0002), for a compact 16-byte unique index consistent with every other id. **Fallback:** `char(26)`/`text COLLATE "C"` if the converter proves awkward (documented if taken).
- **D-WP09-HASH — collision detection over a canonical request hash.** `request_hash` = SHA-256 (BCL) over a **culture-invariant canonical serialization of the semantic command** (target + normalized fields: integer minor units, ISO-8601 dates, lines in a defined order) — not raw request bytes, to avoid whitespace/ordering false-collisions. The canonicalization is documented and unit-tested for determinism.
- **D-WP09-LAYER — enforce in the posting transaction, not an HTTP middleware.** Exactly-once requires the key row to commit **atomically** with the entry; a pre-handler HTTP middleware cannot share the DB transaction, so it would leave a window for double-post or orphan keys. WP09 therefore implements the check/store inside `JournalPostingService.ExecuteAsync` (Infrastructure) and keeps only header **validation** at the endpoint. This is the correct reading of the spec's "idempotency middleware" (the guarantee, not the literal ASP.NET component); the deviation + rationale are recorded here.
- **D-WP09-CLEANUP — cleanup runs on the owner connection, cross-tenant.** The TTL purge deletes across all spaces, so it runs on the base login (which is not under `FORCE`d `leafledger_app` RLS) rather than binding a single space. If a production deployment uses a non-owner base login, the purge needs an explicit grant or a `SECURITY DEFINER` function — recorded as a note, not built now.

## Golden fixtures

**None required, and none created.** WP09 ports no OLD accounting function; the OLD system had *no* idempotency mechanism (weaknesses §1.5). Correctness is pinned by:
- **integration tests** (replay / collision / expiry / concurrency / cross-space isolation over the real `postgres:17` stack),
- a **request-hash determinism** unit test, and
- the **WP08 I5 property** ("retried posts exactly-once").
Recorded explicitly so QA does not expect a golden artifact (mirrors the WP06/WP07 precedent for spec-derived mechanisms).

## Dependencies

- **No new production NuGet.** SHA-256 = `System.Security.Cryptography`; the metric = `System.Diagnostics.Metrics`; the cleanup = a BCL `BackgroundService`; DB access = the existing Npgsql/EF stack; the ULID↔uuid converter already exists in SharedKernel (N1). The test project already has `Microsoft.AspNetCore.Mvc.Testing` / `Microsoft.AspNetCore.TestHost` + the Docker fixture.
- The P1-WP04 OpenAPI pipeline regenerates the contract (required header + 409 + 400 responses); the CI `contract` diff gate must stay green.

## File list (implementation target)

**New — `backend/src/LeafLedger.Modules.Ledger/Infrastructure/Migrations/`**
- `<timestamp>_IdempotencyKeys.cs` — raw `migrationBuilder.Sql`: `CREATE TABLE idempotency_keys (...)` + `UNIQUE (space_id, idempotency_key)` + RLS `ENABLE`/`FORCE` + `p_idempotency_keys_isolation` policy + `GRANT SELECT, INSERT … TO leafledger_app`; `Down` drops policy + table. **No `CreateTable`, no EF entity** (keeps `HasPendingModelChanges()` green).

**New — `backend/src/LeafLedger.Modules.Ledger/Infrastructure/`**
- `IdempotencyStore.cs` — ADO helpers (within the bound transaction): `TryGetLiveAsync(space, key)`, `InsertAsync(space, key, actor, target, hash, status, body)`, the 24 h liveness predicate, and the canonical request-hash function (D-WP09-HASH).
- `IdempotencyCleanupService.cs` — a `BackgroundService` running the cross-tenant TTL `DELETE` on the base connection (D-WP09-CLEANUP), interval a documented constant.
- `IdempotencyMetrics.cs` — the `leafledger.idempotency.collisions` counter (registered via `IMeterFactory`).

**Modified**
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/JournalPostingService.cs` — implement the enforce/replay/store logic in `ExecuteAsync` (the `idempotencyKey` parameter becomes live); collision → 409 + metric; replay → stored status/body; store on success in-transaction.
- `backend/src/LeafLedger.Modules.Ledger/Application/Posting/PostingContracts.cs` (+ `PostingOutcome`) — carry the replay snapshot (stored `status` + `body`) and a `Replayed` flag so the endpoint can set `Idempotent-Replayed` and return the original status/body.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerEndpoints.cs` — require + validate `Idempotency-Key` (400 on missing/invalid); on replay set `Idempotent-Replayed: true` and return the stored status/body; document the 409/400 responses via `.Produces`.
- `backend/src/LeafLedger.Modules.Ledger/Infrastructure/LedgerModule.cs` — register `IdempotencyStore`, `IdempotencyMetrics`, and the cleanup `BackgroundService`.
- `backend/src/LeafLedger.Host/Program.cs` — register the meter (`AddMetrics`/`IMeterFactory`) and the hosted cleanup service if not registered by the module.
- `backend/openapi/leafledger-v1.json` — regenerated (required `Idempotency-Key` header + 409 collision + 400 validation on the two writes).
- `app/src/api/schema.d.ts` (+ `client.ts` only if the generator changes it) — regenerated; **do not hand-edit**.
- `docs/rebuild/plans/P2-WP09-idempotency-middleware.md` + `docs/rebuild/status.md` — notes/state.

**New — tests**
- `backend/tests/LeafLedger.IntegrationTests/Ledger/LedgerIdempotencyTests.cs` (`[Trait("Category","Integration")]`) — real HTTP + `postgres:17`: (1) same key + identical payload → replay, **one** `entry_no`, `Idempotent-Replayed: true`; (2) same key + different payload → **409** `idempotency.key_reused` + the collision counter increments (test `MeterListener`); (3) a row aged past 24 h → the same key behaves as fresh (a second entry is created); (4) **concurrent** duplicate posts (two parallel calls, same key) → exactly one entry; (5) reverse endpoint is idempotent the same way; (6) missing header → 400 `idempotency.key_required`, malformed → 400 `idempotency.key_invalid`; (7) a key stored under space A never replays for space B (RLS second wall).
- `backend/tests/LeafLedger.IntegrationTests/Ledger/IdempotencyCleanupTests.cs` (`[Trait("Category","Integration")]`) — inserting an aged row then running one cleanup pass deletes it; a fresh row survives.
- `backend/tests/LeafLedger.Modules.Ledger.Tests/IdempotencyHashTests.cs` (unit) — the canonical request hash is deterministic (identical semantic payload → identical hash across processes/OSes/cultures), order-normalized, and differs on any material field change.
- **Update** `backend/tests/LeafLedger.IntegrationTests/Ledger/LedgerHttpEndpointTests.cs` — send a valid `Idempotency-Key` on the existing post/reverse calls (test-only, per D-WP09-REQ).

No `*.Domain` change; no `CreateTable`/EF-modelled idempotency entity; no frontend beyond regenerated `api/**`.

## Boundary note

- The migration + store + cleanup + metric live in the Ledger module's **Infrastructure** (the module owns its schema per the D1 single-context baseline); header validation lives at the endpoint (Host-mapped). The arch tests (`EfCoreIsConfinedToInfrastructureNamespaces`, `DomainNamespacesDependOnlyOnSharedKernel`) must stay green — nothing touches `*.Domain`.
- Enforcement runs **inside** the WP05/WP06 txn-local binding, so the key row is space-scoped and RLS-isolated with no extra binding logic.

## Implementation sequence

1. Add the `IdempotencyKeys` raw-SQL migration (table + unique index + RLS + grants); confirm `HasPendingModelChanges()` stays false and the table is reachable under a bound space.
2. Add `IdempotencyStore` + the canonical hash + its determinism unit test (red → green) before wiring the service.
3. Wire `ExecuteAsync`: lookup → replay / collision / expiry / proceed-and-store, all in-transaction; add the collision metric.
4. Make the header required + validated at the endpoint; set `Idempotent-Replayed` on replay; regenerate the OpenAPI contract + TS client; update the WP05 HTTP tests to send a key.
5. Add `IdempotencyCleanupService` + its cleanup test.
6. Add `LedgerIdempotencyTests` (replay / collision+metric / expiry / concurrency / reverse / 400s / cross-space RLS).
7. Run Release build + arch + full suite (incl. integration under Docker) + lint/typecheck/page-budget + contract gate; document results/deviations; move to `verify`.

## Acceptance criteria (concrete tests)

1. **Build, boundaries & contract:** `dotnet build LeafLedger.sln -c Release` = 0/0; architecture tests stay green; `HasPendingModelChanges()` remains **false** after the migration (raw SQL, no EF entity); the CI `contract` gate is green after regenerating the OpenAPI + TS client (required `Idempotency-Key` header + 409 + 400 on the two writes, no unintended drift).
2. **Replay — retried POST, same key + same payload → no double posting (N2 AC1):** a second identical `POST …/journal-entries` with the same `Idempotency-Key` returns the **original** response (same entry id / `entry_no`, same status/body) with `Idempotent-Replayed: true`; the trial-balance total and the max `entry_no` are unchanged (asserted directly). The reverse endpoint behaves identically.
3. **Collision — same key + different payload → reject + metric (N2 AC2):** a `POST` reusing a key with a materially different payload returns **409** `idempotency.key_reused` (`application/problem+json`) and increments `leafledger.idempotency.collisions` (verified via a test `MeterListener`); no entry is created.
4. **Expiry — expired key behaves as fresh (N2 AC3):** a key row aged past the 24 h TTL is treated as absent — the same key on a new request creates a new entry; and one `IdempotencyCleanupService` pass deletes aged rows while leaving fresh rows (cleanup test).
5. **Required + validated header:** a write with **no** `Idempotency-Key` → **400** `idempotency.key_required`; a malformed (non-ULID) key → **400** `idempotency.key_invalid`; both `application/problem+json`. Reads/reports are unaffected.
6. **Concurrency — exactly-once under parallel retries:** two concurrent `POST`s with the same key + payload produce **exactly one** entry (one `entry_no`), the other replaying; no unique-violation surfaces to the client as a 500.
7. **Atomicity:** the key row and the journal entry commit **together** — if the operation fails (e.g. unbalanced 422), **no** key row is written (a later retry with a corrected payload under the same key is allowed, not a collision); if the key row can't be written, the entry is not committed.
8. **RLS second wall on keys:** a key stored under space A is invisible/unusable for space B — a space-B request with the same key value does **not** replay space A's response (the `idempotency_keys` isolation policy is proven, mirroring the other tenant tables); a no-context connection is fail-closed.
9. **Scope containment:** `git diff --name-only` limited to the file list; the migration adds **only** the `idempotency_keys` table + index + RLS + grants (no change to existing tables/triggers/policies — verified by the no-drift test); no `*.Domain` change, no frontend beyond regenerated `api/**`, no new production NuGet; the existing Ledger integration suite stays green.
10. **ProblemDetails shape:** the 400/409 responses use `application/problem+json` with the stable codes above and no PII/stack traces; a successful (non-replay) write is unchanged from WP05; a replay returns the original 201 body + `Idempotent-Replayed: true`.
11. **Determinism:** the request-hash unit test proves identical semantic payloads hash identically (culture/OS/whitespace/line-order invariant) and any material change alters the hash.
12. **Quality gate:** lint, typecheck, page budgets, arch/boundary tests, and the relevant unit + idempotency integration tests all pass in Release; integration runs under Docker (`postgres:17`) via the `Category=Integration` filter and is green on the main-branch job.

## Definition of done

All 12 ACs pass; the two write endpoints are exactly-once under retries with atomic key+entry commit, replay, 409 collision + metric, 24 h TTL cleanup, a required+validated ULID header, and RLS-isolated keys; the migration adds only the new table with no model drift; the contract regenerates green; no `Domain`/frontend/production-NuGet change. Then state → `verify` and route to LL QA Reviewer. On PASS, the WP05 write endpoints clear their "not production-ready until WP06 + WP09" gate, and the WP08 **I5** exactly-once property becomes implementable.

## Sequencing

WP09 is a direct successor to WP05 and is **independent of WP10** (period lifecycle) — it can be implemented immediately against current `main`. The recommended Phase-2 finish order is **WP09 → WP10 → WP08** (WP08's I5 needs WP09; its I3 needs WP10). WP10's period-write endpoints should opt into this same idempotency seam (a WP10 note), reusing `IdempotencyStore` rather than re-implementing it.

## Risks / notes

- **Size is at the limit.** Migration + service wiring + cleanup + metric + header validation + tests is a full ≤ 2-day WP. If the concurrency/replay edge cases balloon, a clean split is **WP09a** (table + replay + collision + required header — the exactly-once core) and **WP09b** (TTL cleanup + metric wiring); prefer the whole WP if it fits. Record any split in status.md.
- **Atomicity is the crux (D-WP09-LAYER).** The key row *must* be written in the same transaction as the entry; an HTTP-middleware or post-response write leaves a double-post/orphan window. AC7 is the tripwire — do not relax it.
- **Concurrency correctness.** Rely on the `UNIQUE (space_id, key)` index + the row lock for serialization; a unique-violation on the race must be caught and turned into a **replay** (read the committed row), never a 500. AC6 pins this.
- **Cleanup privilege (D-WP09-CLEANUP).** The cross-tenant purge runs on the owner connection because RLS would otherwise fail-closed with no space context. If prod uses a restricted base login, the purge needs an explicit grant or a `SECURITY DEFINER` function — flagged, not built here.
- **Replay fidelity.** Store the full `response_body jsonb` + `response_status` so a replay is byte-identical (including the `Location`/entry id); do not recompute the response on replay (the ledger may have advanced). AC2 asserts identical body.
- **Required-header contract tightening.** Making the header required (D-WP09-REQ) changes the WP05 tests + the OpenAPI contract; this is intentional per non-negotiable #4 and safe because the endpoints are not yet production-exposed. The contract diff must be reviewed as an *intended* change, not accidental drift.
- **Metric, not alerting.** Only the counter is emitted; dashboard + alert wiring is an observability carry-forward (N2's "standing alert" is ops, not this WP's code).

## Implementation notes

- **2026-07-12 — LL Backend Dev:** Implemented the raw-SQL migration, deterministic canonical SHA-256 hashing, transaction-local same-key locking/replay/collision/expiry handling, atomic response storage, required ULID header validation, collision counter, owner-connection cleanup service, OpenAPI/TS regeneration, and focused unit/HTTP/cleanup coverage. Fixed the replay JSONB deserialization to preserve camelCase response values, including the original entry ID. Docker-backed validation is now green: full Release backend suite **242/242** (SharedKernel 45, ChartOfAccounts 33, Ledger 68, architecture 3, integration 93), with zero failures. State → `verify`; next LL QA Reviewer.
- **2026-07-12 — LL Backend Dev:** Addressed QA findings. Cross-tenant cleanup now calls a narrowly scoped `SECURITY DEFINER` function rather than issuing a direct `DELETE` from the hosted service; the cleanup test proves invocation through `leafledger_app`. Added HTTP acceptance coverage for reverse replay, expired-key reuse, concurrent duplicate posts, failed-post key non-reservation, and cross-space key isolation. Full Release backend suite is green: **247/247** (45 SharedKernel, 33 ChartOfAccounts, 68 Ledger, 3 architecture, 98 integration). State remains `verify`; next LL QA Reviewer.
