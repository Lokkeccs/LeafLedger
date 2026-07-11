# Architecture Decision Records (ADR log)

This is the log of **Architecture Decision Records** for the LeafLedger rebuild — the durable, reviewable
record of *why* the system is the way it is. Each ADR captures one decision: its context, the decision
itself, its consequences, and the alternatives rejected.

## Conventions
- **Numbering:** `ADR-NNNN`, zero-padded, **monotonic and never reused**. A superseded ADR keeps its
  number and is marked superseded; a new ADR records the replacement.
- **Filename:** `ADR-NNNN-<kebab-title>.md`.
- **Status lifecycle:** `proposed → accepted → superseded by ADR-XXXX`.
- **Template:** copy [`TEMPLATE.md`](./TEMPLATE.md) — sections: Context / Decision / Consequences /
  Alternatives considered, over a Status / Date / Deciders / Related header.
- **When to write one:** any decision that shapes architecture, storage, contracts, or tooling — or that
  records a golden-fixture divergence or resolves a spec ambiguity.

> **Numbering note:** ADR-0002 (ID storage) predates ADR-0001 because the ID-storage question was
> settled first, inside P1-WP03, before any foundational ADR file existed. ADR-0001 backfills the
> foundational 2026-07-03 decision. The out-of-order dates are intentional, not an error.

## Log
| ADR | Title | Status | Date | Related WP |
|---|---|---|---|---|
| [ADR-0001](./ADR-0001-online-first-postgres.md) | Online-first architecture + PostgreSQL as system of record (fresh-start launch) | accepted | 2026-07-03 | P1-WP05 |
| [ADR-0002](./ADR-0002-id-storage.md) | Identifier storage — ULID values in `uuid` columns, prefix at the boundary | accepted | 2026-07-06 | P1-WP03 |
| [ADR-0003](./ADR-0003-api-contract-pipeline.md) | API contract pipeline — build-time OpenAPI → generated TypeScript client | accepted | 2026-07-06 | P1-WP04 |
| ADR-0004 | *(reserved)* Single migration `DbContext` for the Phase-2 baseline; per-module split deferred | reserved | — | P2-WP02 (N-D1) |
| [ADR-0005](./ADR-0005-posting-requires-open-period.md) | Posting requires a defined, open period — deliberate divergence from old "no period ⇒ allowed" | accepted | 2026-07-11 | P2-WP04 (A1) |

> **ADR-0004 is reserved, not skipped:** the single-context-baseline decision (P2-WP02 N-D1) was
> earmarked as ADR-0004 before ADR-0005 was written. It stays reserved until LL Docs Editor drafts it;
> the number is not reused.
