# ADR-0002: Identifier storage — ULID values in `uuid` columns, prefix at the boundary

- **Status:** accepted
- **Date:** 2026-07-06
- **Deciders:** LL Backend Dev (implementing), LL Architect (planned)
- **Related:** WP P1-WP03; risk-review N1 ([NOTES-risk-review-2026-07-06.md](../../rebuild/plans/NOTES-risk-review-2026-07-06.md)); target architecture §4.1

## Context
The system uses prefixed ULIDs as public identifiers (`je_…`, `acc_…`): sortable, collision-free,
and free of the numeric-id ↔ global-id impedance that plagued the old system. Two questions had to
be settled before any entity type exists:

1. **Physical storage type.** ULIDs can be stored as `text` (26-char Crockford base32) or as the
   16-byte binary form. Postgres has a native 16-byte `uuid` type with compact, index-friendly
   storage and byte-wise ordering.
2. **Where the prefix lives.** The `xxx_` prefix is meaningful to API consumers but carries no
   information the database needs.

A subtlety forces a decision now rather than later: .NET's `Guid.ToByteArray()` is little-endian for
the first three fields, so a naïve ULID→`Guid` conversion can *invert* ordering relative to Postgres
`uuid` (which compares its 16 bytes big-endian). If we get this wrong, `uuid`-indexed reads would not
follow creation/temporal order, silently defeating one of ULID's main benefits.

## Decision
We will:

- **Store identifiers as Postgres `uuid` (16 bytes).** The canonical in-memory value is a ULID; the
  stored value is its 16 bytes.
- **Keep the prefix strictly at the API boundary.** `Id<T>.ToBoundaryString()` adds the per-kind
  prefix; `Id<T>.ParseBoundary()` validates and strips it. The prefix never reaches storage, and
  `Id<T>.ToStorage()` returns a bare `Guid`.
- **Convert ULID↔`Guid` big-endian** so byte-wise `uuid` ordering in Postgres matches ULID order.
  Implemented with `new Guid(bytes, bigEndian: true)` and `Guid.TryWriteBytes(…, bigEndian: true, …)`
  (.NET 8+), pinned by a property test over thousands of identifiers
  ([IdTests.cs](../../../backend/tests/LeafLedger.SharedKernel.Tests/IdTests.cs)).

## Consequences
- **Positive:** Compact, index-friendly storage; `uuid` ordering follows creation order; no prefix
  bytes wasted in the database; the boundary format stays human-friendly and type-checked.
- **Positive:** The order-preservation invariant is guarded by an automated property test, so the
  endianness trap cannot silently regress.
- **Neutral:** The per-kind prefix is defined by each entity's `IEntityTag.Prefix`; adding an entity
  type means adding a tag. EF value-converter wiring (Guid ↔ column) is deferred to Phase 2.
- **Negative:** Raw `uuid` values in the database are not self-describing (no visible prefix); tooling
  that inspects rows must know the entity type. Accepted — the boundary layer always re-attaches the
  prefix.

## Alternatives considered
- **Store as `text` with prefix.** Rejected: larger rows/indexes, and it bakes a presentation concern
  into storage.
- **Store as `text` without prefix (26-char base32).** Rejected: still larger than `uuid` and gives up
  the native type's tooling and comparison semantics.
- **Rely on the default `Guid` byte order.** Rejected: little-endian field order breaks `uuid` sort
  ordering versus ULID order — the specific trap this ADR exists to avoid.
