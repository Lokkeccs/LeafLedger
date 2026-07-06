# P1-WP03 — SharedKernel value types: Money, Ids, Period, Result

- **Phase:** 1 (foundation)
- **State:** done (QA PASS 2026-07-06 — F1 fixed and re-verified)
- **Owner (implementation):** LL Backend Dev
- **Depends on:** P1-WP01 (repo scaffold), P1-WP02 (compose) — both done/merged
- **Estimated size:** ≤ 2 days
- **Spec sources:** `docs/architecture/rebuild/03-target-architecture.md` §4.1 (IDs, Money), §5 (SharedKernel surface); `docs/architecture/rebuild/04-implementation-plan.md` §5 (porting strategy); `docs/rebuild/plans/NOTES-risk-review-2026-07-06.md` (N1, ID storage)

## Goal
Populate the currently-empty `LeafLedger.SharedKernel` with the four foundational, dependency-light value types every module will build on:
`Money` (integer minor units + ISO currency), typed `Id<T>` (ULID-backed, stored as `uuid`), `Period` (immutable accounting-period value), and `Result` / `Result<T>` (functional outcome type). Establish the **ID-storage decision** from risk-review N1 as code + an ADR.

## Scope (what this WP delivers)
1. **Money** — immutable value type: `long` minor units + a `CurrencyCode` (validated ISO-4217 alpha-3). Operations: `Zero(currency)`, add, subtract, negate, compare/equality, `IsZero`. Cross-currency arithmetic throws (programming error, not an expected failure). Edge conversion helpers `FromDecimal(decimal, CurrencyCode)` / `ToDecimal()` using each currency's minor-unit exponent and a **single, documented rounding convention** (see Open Question A). **No `float`/`double` anywhere in the API surface.**
2. **`CurrencyCode`** — small validated value type (3 uppercase letters + known minor-unit exponent lookup for the currencies in scope: at minimum CHF, EUR, USD, JPY). Invalid codes rejected at construction.
3. **Typed IDs** — `Id<T>` (or per-entity structs) backed by a ULID. **Storage representation is `uuid` (16 bytes); the `xxx_` prefix is an API-boundary concern only** (N1). Provides: generate (monotonic), ULID↔`Guid` lossless + sort-order-preserving conversion, parse/format with prefix at the boundary, and prefix-mismatch rejection.
4. **Period** — immutable value type: a half-open date range `[start, endExclusive)` plus a `PeriodStatus { Open, Closed }` enum. Operations: `Contains(DateOnly)`, comparison/equality, ordering. (The *lifecycle* that flips a period Open→Closed and the period-close engine are Phase 2 — see Non-goals.)
5. **Result / Result<T>** — functional outcome: success value or an `Error` (code + message + optional category). `Map`, `Bind`, `Match`, `IsSuccess`/`IsFailure`. Failure path throws no exceptions.
6. **ADR** — `docs/architecture/adr/ADR-0002-id-storage.md` recording: column type `uuid`, prefix-at-boundary policy, and rationale (N1 requires an ADR draft). (ADR-0001 is reserved for P1-WP05's online-first/Postgres decision; ADR index/log is formalized in P1-WP05.)
7. **Test project** — new `LeafLedger.SharedKernel.Tests` (xunit) added to `LeafLedger.sln`, including property tests for the ID sort-order/round-trip invariants.

## Non-goals (explicitly deferred)
- **No EF Core / DbContext / schema / migrations / value converters** — persistence wiring is P2. (Arch test already forbids EF outside `*.Infrastructure`.)
- **No posting rules, no FX conversion/revaluation, no VAT, no `fxPolicy`/`postingValidity`/`periodCloseEngine` semantics** — Phase 2, ported from OLD code under golden fixtures.
- **No Money allocation / distribution / largest-remainder split** — this is accounting-sensitive rounding used by attributions (share_permille) and belongs to Phase 2 where it will be pinned by golden fixtures. If a caller needs it earlier, raise a new WP; do not smuggle it in here.
- **No Rappen (CHF 0.05) cash rounding** — that is a settlement/display concern (later phase), not the minor-unit value type.
- **No period-close lifecycle / fiscal-calendar construction** — `Period` is a pure range+status value here; fiscal-year rules are Phase 2.
- No API endpoints, no serialization contracts beyond the ID prefix helpers.

## Source material (salvage vs rewrite)
Per `04-implementation-plan.md` §5, these foundational types are **rewrite, not port**: the OLD system used float/number amounts and string dates that the new architecture deliberately replaces with exact integer money and typed IDs. There is therefore **no old behavior to preserve at this layer** — the behavioral porting (FX/VAT/period-close rounding) happens in Phase 2 with golden fixtures.
- OLD repo (`Lokkeccs/Accounting`, read-only) was **not reachable during planning** (private to the planning tool). This does **not** block the WP because nothing is being ported here. If the Backend Dev discovers a salvageable currency/exponent table in OLD code, copying the *data* (currency→exponent) is fine; copying *float rounding logic* is not.

## Golden fixtures
**None required for this WP.** Rationale: (a) foundational value types are a greenfield rewrite (above); (b) the only accounting-sensitive decision — the decimal→minor rounding convention — is a *forward* decision (Open Question A) confirmed by the LL Accounting Expert and pinned by property/example tests, not extracted from OLD behavior; (c) every fixture-pinned behavior (allocation, FX, VAT, period close) is a Non-goal deferred to Phase 2. Phase 2 WPs that port those will route fixture extraction to **LL Fixture Smith**.

## Open questions — answered by LL Accounting Expert (2026-07-06)
- **A. Rounding convention — RESOLVED: `MidpointRounding.AwayFromZero` (Swiss commercial rounding / "kaufmännische Rundung").**
  Switzerland uses commercial rounding (round half away from zero), not banker's (half-to-even), for invoices, VAT, and hand-reconcilable figures; banker's rounding would produce totals that don't match ESTV worksheets and hurt auditability. Constraints for implementation:
  - This is the **base default for the low-level `FromDecimal` helper only**. Legally-specific rounding (VAT line/total per ESTV MWSTG practice, FX conversion) is **Phase 2**; those policies set their rounding explicitly where the law dictates — no reliance on a hidden default.
  - The helper's rounding mode must be **explicit** in the API surface (no silent banker's default), so Phase 2 callers cannot be surprised.
  - CHF 0.05 Rappenrundung remains out of scope (cash-settlement concern — already a Non-goal).
- **B. Period granularity — RESOLVED: keep `Period` a plain half-open `[start, endExclusive)` range + `PeriodStatus`; do NOT model the fiscal calendar at this layer.**
  OR art. 958 permits non-calendar fiscal years and short/long first/last business years, so the domain needs variable-length periods — but a raw half-open range is exactly the primitive that supports all of that without baking in calendar assumptions. Fiscal-year derivation (configured FY start, short first/last year, monthly sub-periods, locking) is **Phase 2** logic that *produces* `Period` values. Implementation invariants to enforce:
  - Reject empty/inverted ranges: require `start < endExclusive`.
  - Civil-date semantics: use `DateOnly` (Europe/Zurich calendar dates), not instants/timezones.

## Dependencies to add (record per repo rule "no new dependencies without a plan-file entry")
- **`Ulid`** (Cysharp/Ulid) — well-tested ULID struct with `ToGuid()`/`FromGuid()` that preserves byte order and lexicographic sort, and a monotonic factory. Added to `LeafLedger.SharedKernel`. *(Acceptable alternative: a minimal in-house ULID if the team wants zero deps in the kernel; the sort-order property test is the gate either way.)*
- **Property-testing library** for the test project only — `CsCheck` (pure C#, no F# dependency) preferred, or `FsCheck.Xunit`. Choose one; record in the test csproj.

## File list (implementation targets — LL Backend Dev)
- `backend/src/LeafLedger.SharedKernel/Money/Money.cs`
- `backend/src/LeafLedger.SharedKernel/Money/CurrencyCode.cs`
- `backend/src/LeafLedger.SharedKernel/Ids/Id.cs` (generic `Id<T>` + generation/parse/format helpers)
- `backend/src/LeafLedger.SharedKernel/Time/Period.cs` (+ `PeriodStatus`)
- `backend/src/LeafLedger.SharedKernel/Results/Result.cs` (+ `Result<T>`, `Error`)
- `backend/src/LeafLedger.SharedKernel/LeafLedger.SharedKernel.csproj` (add `Ulid` package ref; remove the placeholder comment)
- `backend/tests/LeafLedger.SharedKernel.Tests/LeafLedger.SharedKernel.Tests.csproj` (new)
- `backend/tests/LeafLedger.SharedKernel.Tests/{MoneyTests,CurrencyCodeTests,IdTests,PeriodTests,ResultTests}.cs`
- `backend/LeafLedger.sln` (add the new test project)
- `docs/architecture/adr/ADR-0002-id-storage.md` (new; from `docs/architecture/adr/TEMPLATE.md`)

## Acceptance criteria (as concrete tests — all must pass)
**Money / CurrencyCode**
1. `Money` equality is by `(minorUnits, currency)`; type is immutable (readonly).
2. Add/subtract/negate produce correct minor-unit results; adding two different currencies **throws** and no partial value is produced.
3. `FromDecimal`/`ToDecimal` round-trip for an exponent-2 currency (CHF, EUR) and an exponent-0 currency (JPY): `ToDecimal(FromDecimal(d, c)) == expected` for representative values including negatives and the rounding boundary, using **`MidpointRounding.AwayFromZero`** (Open Question A) — e.g. `1.005 CHF → 101` minor, `-1.005 CHF → -101` minor. Property test: `FromDecimal(ToDecimal(m), m.Currency) == m`. The rounding mode is an **explicit** parameter/constant on the API surface, not a hidden default.
4. Reflection/analyzer test proves **no `float` or `double` member** exists on `Money` or `CurrencyCode`'s public surface.
5. `CurrencyCode` rejects non-alpha-3 / unknown codes at construction; accepts CHF/EUR/USD/JPY with correct exponents.

**Ids (N1 — verbatim acceptance criteria)**
6. `Id<T>` (or equivalent) round-trips ULID ↔ `uuid` **losslessly**, **preserving lexicographic sort order** (property test: for a monotonically-generated ULID sequence, the mapped `Guid`/uuid values sort into the same order — the invariant that keeps Postgres `uuid` index order aligned with creation order).
7. Prefix handling is **API-boundary-only**; a unit test proves the storage/binary representation contains **no prefix** and the prefix is added only by the boundary format helper.
8. Parsing a string with a wrong/missing prefix, or a malformed ULID, is rejected (typed failure); a correct `xxx_<ulid>` round-trips through parse→format.
9. ADR `ADR-0002-id-storage.md` exists and states column type (`uuid`), prefix policy, and rationale.

**Period**
10. `Contains` is correct at both bounds of `[start, endExclusive)` (start inclusive, end exclusive); equality and ordering behave; `PeriodStatus` enum exists (lifecycle not exercised). Construction with `start >= endExclusive` is rejected (Open Question B); the type uses `DateOnly` (civil dates, no timezone).

**Result**
11. Success carries the value and `IsSuccess`; failure carries an `Error`, `IsFailure`, and no value; `Map`/`Bind` short-circuit on failure; `Match` dispatches to the correct branch; the failure path throws no exception (unit test).

**Cross-cutting / gates**
12. `LeafLedger.SharedKernel.Tests` is in the solution; `dotnet test --filter "Category!=Integration"` is green.
13. Architecture tests still pass — `SharedKernel` references **no other LeafLedger assembly** (the external `Ulid` package is allowed).
14. Release build is **0 warnings / 0 errors** (TreatWarningsAsErrors is on).

## Manual / human steps
- LL Accounting Expert answers Open Questions A & B; record the answers in this file before implementation starts.
- After approval, hand to **LL Backend Dev** for TDD implementation.

## Risks & notes
- **Guid endianness trap:** .NET `Guid.ToByteArray()` is little-endian for the first three groups, so a naive ULID→Guid can *invert* sort order versus Postgres `uuid` (byte-wise big-endian). AC #6's property test is the guard; Cysharp `Ulid.ToGuid()` is designed to preserve order — validate, don't assume.
- **Scope pressure:** four types in one WP is the upper bound of ≤2 days; each is deliberately minimal (no allocation, no FX, no lifecycle). If implementation reveals any of them needs accounting-behavior decisions beyond Open Questions A/B, **stop and add a WP** rather than inventing behavior.
- **Adding a NuGet dep to the kernel** is acceptable (`Ulid` is tiny and dependency-free); the arch test only forbids inter-*LeafLedger* dependencies.

## Implementation notes
- **2026-07-06 (LL Backend Dev):** Implemented all four value types + ADR. Results:
  - Solution Release build **0 warnings / 0 errors**; `SharedKernel.Tests` **44/44 pass**; architecture tests **3/3 pass** (SharedKernel references no other LeafLedger assembly; external `Ulid` allowed). ACs 1–14 satisfied.
  - Files: [Money.cs](../../../backend/src/LeafLedger.SharedKernel/Money/Money.cs), [CurrencyCode.cs](../../../backend/src/LeafLedger.SharedKernel/Money/CurrencyCode.cs), [Id.cs](../../../backend/src/LeafLedger.SharedKernel/Ids/Id.cs), [Period.cs](../../../backend/src/LeafLedger.SharedKernel/Time/Period.cs), [Result.cs](../../../backend/src/LeafLedger.SharedKernel/Results/Result.cs); tests under [SharedKernel.Tests](../../../backend/tests/LeafLedger.SharedKernel.Tests); [ADR-0002](../../architecture/adr/ADR-0002-id-storage.md).
  - Money uses `MidpointRounding.AwayFromZero` as an explicit `FromDecimal` parameter (Open Question A); `1.005 CHF → 101`, `-1.005 → -101` verified; `checked` arithmetic guards overflow.
  - **Guid conversion done in-house** with the .NET 8+ big-endian `Guid(bytes, bigEndian:true)` / `TryWriteBytes(… bigEndian:true …)` rather than relying on `Ulid.ToGuid()`, to guarantee Postgres-`uuid` order matches ULID order; pinned by a 5,000-id property test (the endianness trap in Risks). `Ulid` (Cysharp) v1.3.4 used only for ULID mechanics (generation, base32 parse/format, 16-byte form).
  - **Deviations from plan (all in-scope, noted for QA):**
    1. Test property assertions use deterministic generative loops in plain xUnit (thousands of iterations) rather than adding **CsCheck** — keeps the kernel test project dependency-light; the plan's Dependencies section permitted the in-house route.
    2. Renamed the error record `Error → DomainError` to satisfy CA1716 (reserved-keyword clash); the `Result.Error` property name is unchanged.
    3. `[SuppressMessage(CA1000)]` on `Id<T>` and `Result<T>` — static factory methods are the idiomatic construction API for these value types.
    4. `NoWarn CA1707` in the **test project only** — underscored xUnit test names.
  - No EF/DbContext/allocation/FX/period-close introduced (Non-goals respected).

## QA verdict
**PASS — 2026-07-06 (LL QA Reviewer, re-verify).** F1 resolved; all acceptance criteria met.

Re-verification (my run, after fix):
- `dotnet build LeafLedger.sln -c Release` → **0 warnings / 0 errors**.
- `dotnet test LeafLedger.sln -c Release --filter "Category!=Integration"` → `SharedKernel.Tests` **45/45** (was 44; +1 from the new `[Theory]` case), `ArchitectureTests` **3/3**; integration excluded.
- `git status` → changes confined to the plan's file list; no scope creep.

**F1 (was blocking, AC-4) — CLOSED.** [MoneyTests.cs](../../../backend/tests/LeafLedger.SharedKernel.Tests/MoneyTests.cs) `No_public_member_exposes_float_or_double` is now a `[Theory]` with `[InlineData(typeof(Money))]` and `[InlineData(typeof(CurrencyCode))]`; the reflection assertion (properties, fields, methods, method parameters vs `float`/`double`) runs against both types and passes. AC-4 fully satisfied. Fix was test-only — no application code touched.

All other findings from the prior FAIL remain PASS (Money integer-minor-units/immutable/no-float/cross-currency-throws/`AwayFromZero`; `Id<T>` big-endian uuid round-trip + order-preservation + boundary-only prefix + typed parse failures; ADR-0002; `Period` half-open; `Result`/`Result<T>`; financial integrity; security; hallucination scan; patch-layering scan). Golden fixtures correctly N/A (greenfield).

**Verdict: PASS. State → done.** Cleared for LL Git.

Notes for LL Git (not findings): 3 pre-existing `.github/agents/*.agent.md` **deletions** are in the working tree and are **unrelated** to P1-WP03 — exclude them from this WP's commit.

