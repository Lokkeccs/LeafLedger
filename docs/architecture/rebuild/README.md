# LeafLedger Rebuild Analysis

Complete architectural analysis and rebuild specification, produced 2026-07-03 from a direct audit of the `Accounting/` codebase (frontend, backend, sync subsystem, Cosmos layer, build/deploy, tests, docs).

| Part | Document | Contents |
|---|---|---|
| 1 | [Current Architecture Reconstruction](./01-current-architecture.md) | Domain model (Dexie v58, 45 tables), LWW delta-replication model, Cosmos structure, module boundaries, API surface, UI/state architecture, pipeline, conventions |
| 2 | [Architectural Weaknesses & Problems](./02-weaknesses.md) | Severity-ranked critique: financial-integrity risks, security findings, sync design flaws, Cosmos issues, SOLID/DDD violations, vibe-coding smells, testing/pipeline gaps |
| 3 | [Proposed Target Architecture](./03-target-architecture.md) | **Online-first + PostgreSQL greenfield** (decision 2026-07-03): full stack keep/drop/replace review, relational ledger schema with DB-enforced invariants, modular-monolith backend, TanStack Query frontend, generated API contract |
| 4 | [Implementation Plan & Porting Strategy](./04-implementation-plan.md) | Phases 0–7 (~3.5–4 months, **no data migration — fresh start**), timeline, dependency graph, risks, rollback, salvage/rewrite/discard lists, TS→C# fidelity protocol, final recommendations |
| 5 | [Quality & Maintainability Strategy](./05-quality-and-maintainability.md) | Testing pyramid, CI/CD, review/docs/versioning strategy, long-term boundary enforcement, monitoring |
| 6 | [Feature Roadmap](./06-feature-roadmap.md) | MVP (M1) vs fast-follow (M2) vs post-launch (M3) feature tiers; switch-over readiness model; dropped features |
| 7 | [Vibe-Coding Playbook](./07-vibe-coding-playbook.md) | How to execute the rebuild with agents: per-phase workflow, custom agent definitions, model selection, general instructions, status tracking |

**Decision records:** foundational and per-WP decisions are logged as ADRs — see the [ADR log](../adr/README.md) (ADR-0001 online-first + PostgreSQL, ADR-0002 ID storage, ADR-0003 API contract pipeline).

**Headline conclusions**

1. Two emergencies in the current system regardless of any rebuild: unprotected cross-tenant `/maintenance/*` destruction endpoints, and the complete absence of CI (Phase 0 — do immediately).
2. **Decision (2026-07-03): drop offline-first writes and Cosmos DB; keep React/TS.** This deletes the entire replication problem class (the 11-patch-layer sync onion) instead of solving it, and makes a **full greenfield rebuild (~4–4.5 months)** the recommended path — superseding the earlier hybrid recommendation.
3. Target: PostgreSQL with database-enforced ledger invariants (balance trigger, FKs, RLS, immutable journal + reversals), authoritative C# domain, TanStack Query + generated OpenAPI client frontend, Azure SignalR Service for cache-invalidation pings, Azure Document Intelligence OCR incl. Swiss QR-bill ingestion.
4. The domain logic (posting validity, FX, VAT, period close, subledger generators, import parsing) and ~120 test files are **salvage cargo** — golden fixtures run against both old TS and new C# implementations as the porting oracle.
5. **No data migration** (decision 2026-07-03): the new system launches empty; existing users self-migrate via CSV export/import during a read-only sunset of the old stack.
6. One assumption to validate with usage data before committing: that users do not need offline *writes* (see Part 4, risk #4).
