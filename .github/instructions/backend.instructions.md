---
applyTo: "backend/**"
---
# Backend rules (any agent touching `backend/**`)

These attach automatically to every agent editing backend code, independent of which agent is loaded. They restate the binding architecture so the rules are enforced by context, then by CI — not the honor system.

## Layering & module boundaries (Part 3 §5 — enforced by NetArchTest)
- Modules reference **only** `LeafLedger.SharedKernel` and other modules' **public contracts**. No reaching into another module's internals.
- `*.Domain` references **nothing but SharedKernel** — no EF Core, no ASP.NET, no other module.
- EF Core lives **only** in `*.Infrastructure`. One `DbContext` per module over the shared database.
- Endpoints are thin: parse/authorize → application → map result. No domain logic in endpoints.

## Money & invariants (non-negotiable)
- Money is **integer minor units + ISO `char(3)` currency**. No `double`/`float`/`decimal` arithmetic used as a stand-in for currency amounts anywhere near posting.
- Debits = credits is enforced server-side **and** by the database (deferred constraint trigger). Never weaken or bypass it; client checks are UX only.
- Posted journal entries are **immutable**. Corrections are reversals (`reverses_entry_id`), never UPDATE/DELETE.
- Every write endpoint is **idempotent** (idempotency key, unique-indexed) and **authorized** (scope → license → space role → module permission).
- Tenancy: every business table carries `space_id`; RLS is the second wall behind application checks. Never write a query that could cross tenants.

## Naming conventions
- **IDs:** prefixed ULIDs — `je_…` (journal entry), `acc_…` (account), etc. (Part 3 §4.1). Stored as `text`; sortable, no numeric-id ↔ global-id impedance.
- **Commands** (write intents) are imperative: `PostJournalEntry`, `ReverseJournalEntry`, `CloseAccountingPeriod`.
- **Domain events** are past tense: `JournalEntryPosted`, `PeriodClosed`.
- **Ports** (outbound integration interfaces) end in `Port`; their implementations end in `Adapter` and live in `*.Infrastructure` (e.g. `FxRatePort` → `AlphaVantageFxRateAdapter`).
- **Results:** domain operations return `Result<T>` / domain errors → ProblemDetails at the edge. No exceptions for expected domain failures.
- API routes: `/api/v1/...`, ProblemDetails everywhere, cursor pagination.

## Traceability rule
Never invent accounting behavior. It comes from the WP plan, the target spec, or the OLD code — traceably. If a rule's source is unclear, STOP and ask (route to LL Accounting Expert). Divergence from a golden fixture is resolved by the user via ADR, never silently "fixed".

## Done means
Lint/analyzers clean (warnings are errors), `dotnet build` + `dotnet test` green, boundary (NetArchTest) + financial-invariant tests for the WP pass. No commits — LL Git handles that.
