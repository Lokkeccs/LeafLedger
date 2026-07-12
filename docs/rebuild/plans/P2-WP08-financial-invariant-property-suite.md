# P2-WP08 — Financial-invariant property suite (Phase-2 exit gate)

- **Phase:** 2 (ledger core) — **the exit gate.** Phase 2 is not "done" until this suite is green.
- **State:** planned — **unblocked**. P2-WP09 and P2-WP10 are merged to `main`; the plan is ready for implementation. See [Sequencing / blocker](#sequencing--blocker).
- **Owner (implementation):** LL Backend Dev
- **Depends on:** P2-WP02 (schema + deferred balance trigger + RLS second wall + `postgres:17` Docker fixture), P2-WP03 (ChartOfAccounts domain), P2-WP04 (JournalEntry domain + posting-validity + period-state + exact-integer balance), P2-WP05 (`POST …/journal-entries` (+`/reverse`), eager balance 422, per-space `entry_no`, txn-local binding), P2-WP06 (authorization filter + principal-bound RLS binding + `TestAuthHandler`), P2-WP07 (`trial_balance` view + `GET …/integrity` `balanced` flag — the ≡ 0 oracle), **P2-WP09** (idempotency middleware — the exactly-once oracle), **P2-WP10** (period create/open/close/lock — produces closed/locked periods to reject against).
- **Blocks:** the Phase-2 close-out (Part 4 §Phase 2 exit criterion) and the PR pipeline's `unit + invariant` gate becoming truly flagship-complete (Part 5 §1.1/§1.2).
- **Estimated size:** ≤ 2 days **once WP09 + WP10 are done** (a bounded generative command-sequence engine + a reference model + the six/seven invariant properties + DB-direct trigger/RLS properties + one CI step). Most of the harness — `WebApplicationFactory<Program>`, `LedgerDbFixture` (`postgres:17`), `TestAuthHandler`, seed helpers, txn-local binding — already exists from WP05/WP06/WP07 and is **reused, not rebuilt**. The only genuinely new machinery is the command generator + shrinking/replay + the reference model. Flagged as *at* the size limit — see [Risks](#risks--notes); if the generator engine alone threatens the ≤ 2-day rule, split per the note there.

## Spec sources

- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 2 — **exit criterion (verbatim):** "*property-based invariant suite green — any generated command sequence yields trial balance ≡ 0, unbalanced/closed-period/invalid-ref posts rejected with 422, retried posts exactly-once.*" §5 (rewrite items pinned by tests), §Risk row 7 ("property tests + golden masters" against agent hallucination), §113 ("**Let the database enforce the ledger:** balance trigger, FKs, RLS, immutable journal").
- `docs/architecture/rebuild/05-quality-and-maintainability.md` §1.1 testing pyramid — **Financial invariant / property tests** row (verbatim target): "*Trial balance ≡ 0 for any generated posting sequence; unbalanced/closed-period/invalid-ref posts rejected; idempotent retries exactly-once; FX/VAT rounding bounds; DB balance-trigger + RLS verified directly*", gate = "**PR-blocking, the flagship suite**"; §1.2 CI ("PR pipeline: … unit + **invariant** → contract diff …" — the invariant suite runs on **PR**, not only main).
- ADR-0001 (server + DB are the source of truth), ADR-0002 (uuid ids), **ADR-0005** (posting requires an `open` period — the closed/locked/undefined rejection this suite exercises).
- `docs/rebuild/plans/NOTES-risk-review-2026-07-06.md` **N2** (idempotency lifecycle → WP09, consumed here as the exactly-once oracle) and **N3** (eager balance check + DB second wall → both walls asserted here).
- WP04 QA carry-forward (2026-07-11): "*WP08 pin exact period boundary inclusivity*"; WP05 plan flag: "*`periods.end_exclusive` vs domain inclusive `EndDate` off-by-one*" — pinned by invariant **I7**.

## Goal

Deliver the **flagship, PR-blocking financial-invariant property suite** that certifies Phase-2 ledger correctness at the *system* level (real HTTP + real PostgreSQL + RLS + triggers), by generating bounded random command sequences and asserting that a small set of non-negotiable invariants hold for **every** generated sequence and its shrunk counterexamples:

1. **I1 — Balance ≡ 0.** Any generated sequence of valid posts + reversals leaves the whole-space trial balance at **exactly 0** integer minor units, and `GET …/integrity` reports `balanced = true` with a stable hash.
2. **I2 — Unbalanced rejected.** Any generated *unbalanced* post is rejected **422** (`journal_entry.unbalanced`) and never persisted (trial balance and `entry_no` unchanged).
3. **I3 — Closed/locked/undefined period rejected.** Any generated post whose effective period is closed, locked, or undefined is rejected **422** (`posting_period.not_open` / `posting_period.not_defined`, ADR-0005) and never persisted. *(Needs WP10 to create closed/locked periods.)*
4. **I4 — Invalid reference rejected.** Any generated post referencing a non-existent / cross-space account, or a reverse referencing a non-existent / already-reversed / cross-space entry, is rejected **422/404** (`journal_entry.not_found`, `journal_entry.already_reversed`, …) and never persisted.
5. **I5 — Retries are exactly-once.** Re-sending a generated post with the **same idempotency key + identical payload** yields the *same* entry (replay, no second `entry_no`, no double-post); the same key + a *different* payload is a collision (409/422). *(Needs WP09.)*
6. **I6 — The database is the second wall.** Bypassing the application entirely, a direct unbalanced insert fails at COMMIT on the deferred balance trigger, and a valid principal of space A can never read/write space B's rows (RLS) — asserted directly against `postgres:17`, not through the app.
7. **I7 — Period boundary is exact.** A post dated on the canonical last-included instant of an open period is accepted and one on the first-excluded instant is rejected, pinning the half-open `[start, end_exclusive)` semantics (P1-WP03 `Period`) against the schema/domain off-by-one flagged in WP05.

The suite must be **deterministic and replayable**: each run logs its seed; a failing run reproduces from that seed; failures **shrink** to a minimal counterexample that a human can read. It runs on the **PR pipeline** (Part 5 §1.2), Docker-backed, within a bounded time budget.

## Scope

1. **Generative command engine (new, test-only).**
   - A bounded generator produces sequences of typed commands: `Post(valid)`, `Post(unbalanced)`, `Post(closed-period)`, `Post(invalid-ref)`, `Reverse(existing)`, `Reverse(invalid)`, `Retry(previous post, same/different payload)`, and (once WP10 lands) `ClosePeriod` / `LockPeriod`. Amounts are drawn as integer minor units that **sum to 0** for the valid case and to a non-zero value for the unbalanced case; currencies/accounts are drawn from a small seeded chart so postings are otherwise well-formed.
   - **Shrinking + seeded replay are mandatory.** The chosen engine (see Decision D-WP08-GEN) must minimize a failing sequence to the smallest reproducer and print the seed.
   - Bounded: sequence length and iteration count are constants tuned so the PR step stays within the time budget (see AC 8); the constants are documented.
2. **Reference model (new, test-only).**
   - A pure in-memory model tracks expected per-account net balance, the set of live/reversed `entry_no`s, and per-period open/closed state. After each executed command the suite asserts the **system** (via the WP07 `trial_balance` / `integrity` endpoints and the WP05 post/reverse responses) agrees with the **model** — classic model-based property testing. The model is the arbiter of I1–I5/I7 expectations; it never re-implements posting-validity (that is the system under test).
3. **Full-stack property tests (new).**
   - Execute generated commands against the real API through `WebApplicationFactory<Program>` + `LedgerDbFixture` (`postgres:17`) + `TestAuthHandler`, reusing the WP05/WP06/WP07 seed + binding helpers. Assert I1–I5 + I7 hold for every sequence and every shrunk counterexample; assert rejections carry the exact WP04/WP05 ProblemDetails codes and leave state untouched.
4. **DB-direct invariant tests (I6) — consolidate + property-drive.**
   - The balance-trigger and RLS second-wall behaviours already have point tests (`BalanceTriggerTests`, `RlsTenancyTests`, `ImmutabilityTests` from WP02). WP08 adds the *property* framing: for a generated unbalanced line-set, a direct COMMIT always raises the trigger; for a generated (spaceA, spaceB) pair, an app-role connection bound to A never sees B's rows. Reference the existing tests where they already cover a case; add the generative versions where they do not. No new production DDL.
5. **CI wiring (the flagship gate).**
   - Tag the property tests `[Trait("Category","Property")]`. Add a dedicated **PR** step `dotnet test --filter Category=Property` (Docker-backed; GitHub-hosted runners provide Docker, so Testcontainers works on PR) to `pr.yml` and `main.yml`, and **exclude Property from the plain unit step** so it is not double-counted (unit step filter becomes `Category!=Integration&Category!=Property`). The step is **blocking**. Iteration count is bounded for PR; an optional larger nightly budget is a carry-forward, not this WP.
6. **No production code changes.** WP08 is a *test + CI* work package. If a property fails because production behaviour is wrong (e.g. the I7 off-by-one), the fix belongs to the **owning** WP (WP10 for period boundary, WP05/WP09/WP10 for the relevant slice) as a named finding — WP08 surfaces it, it does not silently patch it.

## Non-goals (explicitly deferred)

- **No FX/VAT rounding-bound properties.** The pyramid row lists "FX/VAT rounding bounds" alongside the ledger invariants, but FX arithmetic/revaluation and VAT are **Phase 4** (no posting-time FX validator exists in Phase 2 beyond WP05's B1 base-amount check). WP08 covers the ledger-core invariants (I1–I7); FX/VAT rounding properties are a Phase-4 amendment to this suite. *(Recorded so QA does not expect FX/VAT here.)*
- **No new production behaviour, endpoints, migrations, or DDL.** WP08 only generates against and asserts on the WP02–WP10 surface. Any production fix is routed to the owning WP.
- **No golden fixtures.** The oracle is the *mathematical invariant* (sum ≡ 0), the *reference model*, and the already-pinned WP01 rule fixtures — not new captured artifacts (see [Golden fixtures](#golden-fixtures)).
- **No E2E / Playwright / browser.** System-level is the API+DB boundary; UI journeys are Phase 3/M1.
- **No load/perf budgets.** Posting-latency / reports-at-100k-lines budgets (pyramid Load/perf row) are the weekly perf job, not this suite.
- **No frontend.** No `app/**` change (not even regenerated `api/**` — WP08 adds no endpoint).
- **No idempotency / period-lifecycle *implementation*.** Those are WP09 / WP10; WP08 only *exercises* them. If they are absent, I5/I3 cannot be written — hence the blocker.

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

### Reference only (not salvaged) — "new capability" per spec
- The OLD system had **no property-based invariant suite** and **no server/DB-enforced balance**; balance was a UI/derived concern over Dexie, and there was no reverse-entry function (corrections were hard deletes — confirmed at WP04 planning). A GitHub search for any generative/property harness or a server-side trial-balance ≡ 0 assertion in the OLD repo returns none. WP08 is therefore a **pure spec-derived rewrite** — the invariant suite is a *new capability the old stack could not express* (Part 4 §113). Nothing is ported.

### Rewrite (spec-derived; no OLD oracle)
- The generator, reference model, invariant properties, and CI wiring are greenfield per Part 4 §Phase 2 + Part 5 §1.1. The *rules* they assert are already pinned: posting-validity/period/currency by the **WP01 golden fixtures** (consumed in WP03/WP04), balance by the **WP02 trigger** + **WP04 exact-integer domain**, rejection codes by **WP05**, exactly-once by **WP09**, period lifecycle by **WP10**. WP08 composes those into a system-level property, it does not redefine any of them.

## Accounting decisions

- **None required as a hard gate.** The invariants are mechanical: a trial balance nets to 0 by double-entry construction; rejection is a mechanical status/code assertion; exactly-once is a replay assertion. No new accounting behaviour is decided here.
- **Period-boundary semantics (I7) — reference, not a new decision.** The canonical semantics were already resolved at **P1-WP03** (LL Accounting Expert answer B): `Period` is a half-open `[start, end_exclusive)` range. WP08's I7 asserts exactly that boundary. The WP05-flagged `end_exclusive` vs domain inclusive `EndDate` discrepancy is an **implementation inconsistency to be resolved in the owning WP** (WP10 period lifecycle, or WP05), which WP08 will surface as a named finding — it is not a fresh accounting question. **If** implementation reveals genuine ambiguity about which instant is "in" a period at a real fiscal-year boundary, route a single narrow question to **LL Accounting Expert** and record the answer here before pinning I7; otherwise the P1-WP03 half-open rule stands.

## Golden fixtures

**None required, and none created.** WP08 ports no OLD accounting function; its oracle is:
- the **mathematical invariant** trial balance ≡ 0 (double-entry, convention-independent — the raw signed `trial_balance` total from WP07),
- the **reference model** (expected per-account balances / live entries / period state), and
- the **already-captured WP01 golden fixtures** that pin the posting-validity / period / currency *rules* the system enforces (consumed in WP03/WP04 — not re-captured here).

This mirrors the WP06/WP07 precedent: a spec-derived, system-level verification WP is pinned by the appropriate testing-pyramid tier (here: property tests), not by new golden artifacts. Recorded explicitly so QA does not expect a golden master.

## Decisions

- **D-WP08-GEN — property engine.** Primary choice: **CsCheck** (MIT, minimal-footprint .NET property library) added as a **test-only** NuGet on `LeafLedger.IntegrationTests` (the project that owns the Docker fixture), for its built-in **shrinking** + seeded replay — essential so a failing flagship gate yields a minimal, reproducible counterexample rather than an opaque random failure. *(New dependency — recorded here per the no-undeclared-dependency rule; test-only, not shipped.)* **Permitted fallback:** the P1-WP03 "generative xUnit" style (no new dependency) **only if** it provides equivalent (a) deterministic seed logging, (b) failing-seed replay, and (c) counterexample minimization; a generator without shrinking is **not** acceptable for the flagship gate. The implementer picks one and records it in the plan notes.
- **D-WP08-CI — the invariant suite runs on PR.** Per Part 5 §1.2 ("unit + invariant" on the PR pipeline) the suite is **PR-blocking**, Docker-backed, bounded. This is a deliberate departure from the WP02 policy that excluded *Testcontainers integration* from PR for speed: the invariant suite is the flagship gate and must guard every PR. Bounded iteration count keeps it within budget (AC 8).

## Dependencies

- **New test-only NuGet:** CsCheck (latest stable) on `LeafLedger.IntegrationTests` — see D-WP08-GEN. No production dependency. (If the generative-xUnit fallback is chosen, **no** new dependency.)
- Reuses the existing stack: `Microsoft.AspNetCore.Mvc.Testing` / `Microsoft.AspNetCore.TestHost`, `Testcontainers.PostgreSql` (`postgres:17`), xunit, Npgsql — all already referenced by `LeafLedger.IntegrationTests`.
- **Hard WP dependencies:** WP09 (I5) and WP10 (I3) must be merged first — see [Sequencing / blocker](#sequencing--blocker).

## File list (implementation target)

**Modified — test project + CI only**
- `backend/tests/LeafLedger.IntegrationTests/LeafLedger.IntegrationTests.csproj` — add the CsCheck test-only `PackageReference` (D-WP08-GEN); no other change. *(Skip if the generative-xUnit fallback is chosen.)*
- `.github/workflows/pr.yml` — add a blocking `dotnet test --filter Category=Property` step (Docker); change the existing unit step filter to `Category!=Integration&Category!=Property` so Property is not double-run.
- `.github/workflows/main.yml` — same Property step (kept blocking on main); adjust the unit/integration filters symmetrically.
- `docs/rebuild/plans/P2-WP08-financial-invariant-property-suite.md` + `docs/rebuild/status.md` — notes/state.

**New — property suite (all `[Trait("Category","Property")]`, under `backend/tests/LeafLedger.IntegrationTests/Ledger/Property/`)**
- `LedgerCommand.cs` — the typed command union (`PostValid`, `PostUnbalanced`, `PostClosedPeriod`, `PostInvalidRef`, `Reverse`, `ReverseInvalid`, `Retry`, `ClosePeriod`/`LockPeriod`) + generators (CsCheck `Gen<…>` or generative-xUnit equivalent).
- `LedgerReferenceModel.cs` — pure in-memory expected state (per-account balance, live/reversed `entry_no` set, per-period state).
- `LedgerSystemDriver.cs` — thin adapter turning a command into the real HTTP call(s) via the shared `WebApplicationFactory`/`LedgerDbFixture`/seed helpers, returning status + parsed ProblemDetails.
- `BalanceInvariantPropertyTests.cs` — **I1** (generated valid post/reverse sequences ⇒ `trial_balance` total 0 + `integrity.balanced=true` + stable hash) and **I2** (generated unbalanced ⇒ 422 `journal_entry.unbalanced`, state unchanged).
- `RejectionInvariantPropertyTests.cs` — **I3** (closed/locked/undefined period ⇒ 422 `posting_period.*`), **I4** (invalid account / reverse target ⇒ 422/404 with the WP05 codes), **I7** (period boundary inclusivity).
- `IdempotencyInvariantPropertyTests.cs` — **I5** (same key+payload ⇒ replay/exactly-once; same key+different payload ⇒ collision) — **requires WP09**.
- `DatabaseSecondWallPropertyTests.cs` — **I6** (generated unbalanced direct insert ⇒ trigger fails at COMMIT; generated cross-space pair ⇒ RLS denial), reusing/extending the WP02 fixtures.

No `app/**`, no `openapi/**`, no `*.Domain`, no migration, no production `Infrastructure`/`Host` change. If any invariant fails against current production code, file a named finding for the owning WP — do **not** edit production code in this WP.

## Boundary note

- WP08 lives entirely in the test tier (`LeafLedger.IntegrationTests`) + CI. The architecture tests (`EfCoreIsConfinedToInfrastructureNamespaces`, `DomainNamespacesDependOnlyOnSharedKernel`) are unaffected and must stay green.
- The property tests exercise the **public** surface only: the WP05 post/reverse endpoints, the WP07 report/integrity endpoints, the WP09 idempotency behaviour, and — for I6 — direct SQL against the schema. They never reach into module internals, preserving module boundaries.

## Implementation sequence

1. **Confirm the blocker is cleared:** WP09 and WP10 are merged to `main`. If not, stop — this WP cannot be completed (see Sequencing / blocker).
2. Pick the engine (D-WP08-GEN); if CsCheck, add the test-only `PackageReference`; write a trivial shrinking smoke property to prove seed-logging + minimization work.
3. Build `LedgerReferenceModel` + `LedgerSystemDriver` + `LedgerCommand` generators over a small seeded chart (reuse WP05/WP06/WP07 seed helpers).
4. Implement **I1/I2** (`BalanceInvariantPropertyTests`) red→green; confirm a deliberately-broken post is shrunk to a minimal counterexample with a printed seed.
5. Implement **I4/I7** then **I3** (`RejectionInvariantPropertyTests`) — I3 against WP10 closed/locked periods; record any I7 boundary discrepancy as a named finding for the owning WP.
6. Implement **I5** (`IdempotencyInvariantPropertyTests`) against WP09.
7. Implement **I6** (`DatabaseSecondWallPropertyTests`) direct against `postgres:17`.
8. Wire the CI `Category=Property` step (pr.yml + main.yml) + adjust the unit filter; prove it is blocking (red-gate: a mutated invariant fails the PR job).
9. Tune iteration/length constants to the time budget; run Release build + full suite (unit + arch + integration + **property**) locally under Docker; document seed(s), results, any named findings; move to `verify`.

## Acceptance criteria (concrete tests)

1. **Build & boundaries.** `dotnet build LeafLedger.sln -c Release` = 0/0; architecture tests stay green; no production `src/**` diff (test + CI + docs only); `git diff --name-only` limited to the file list; no `app/**`, no migration, no DDL.
2. **I1 — Balance ≡ 0 (flagship).** For every generated sequence of valid posts + reversals (≥ the documented iteration count), `GET …/reports/trial-balance` whole-space total = **exactly 0** integer minor units and `GET …/integrity` returns `balanced = true`; two integrity calls on the settled state return a byte-identical `trialBalanceHash`. A deliberately-injected imbalance (mutation test) fails the property and **shrinks** to a minimal, seed-reproducible counterexample.
3. **I2 — Unbalanced rejected.** Every generated unbalanced post ⇒ **422** `journal_entry.unbalanced` (`application/problem+json`, WP05 shape) and the trial-balance total + max `entry_no` are unchanged (never persisted) — asserted against the reference model.
4. **I3 — Closed/locked/undefined period rejected.** Every generated post into a closed/locked/undefined period (periods created via WP10) ⇒ **422** `posting_period.not_open` or `posting_period.not_defined` (ADR-0005) and no state change.
5. **I4 — Invalid reference rejected.** Every generated post to a non-existent/cross-space account ⇒ 422/404; every reverse of a non-existent/already-reversed/cross-space entry ⇒ 422/404 with the WP05 codes (`journal_entry.not_found`, `journal_entry.already_reversed`); no state change.
6. **I5 — Exactly-once (needs WP09).** A generated post replayed with the **same** idempotency key + identical payload returns the same entry id / `entry_no` with no second row; the same key + a different payload ⇒ 409/422 collision; an expired/absent key ⇒ a fresh post. Asserted via the reference model's live-entry set (count invariant).
7. **I6 — DB second wall (direct).** A generated unbalanced line-set inserted directly (bypassing the app) ⇒ COMMIT fails on the deferred balance trigger; an app-role connection bound to space A ⇒ zero rows from space B (RLS), including the no-context fail-closed case — asserted directly against `postgres:17`, not through the API.
8. **I7 — Period boundary exact.** A post dated on the canonical last-included instant of an open period is accepted; a post on the first-excluded instant is rejected — pinning the half-open `[start, end_exclusive)` rule. Any production off-by-one is recorded as a named finding for the owning WP (WP10/WP05), not patched here.
9. **Determinism & shrinking.** Every property logs its seed; a failing property is reproducible from the logged seed and reports a **minimal** counterexample; the suite is culture-invariant (no locale/timezone leakage) and passes with `TZ=UTC`.
10. **CI is PR-blocking & bounded.** A dedicated `Category=Property` step runs on **both** `pr.yml` and `main.yml`, is blocking, runs under Docker (`postgres:17`), and completes within the documented time budget; the unit step no longer double-runs the property tests; a mutated invariant turns the PR job red (red-gate demonstrated).
11. **No production behaviour change.** The suite adds no endpoint, migration, or DDL; if it surfaces a production defect, that is captured as a named finding routed to the owning WP, and this WP's diff remains test + CI + docs only.
12. **Full suite green.** Release build clean; unit + arch + integration + **property** all pass locally under Docker and on CI; the contract gate is untouched (no OpenAPI change).

## Definition of done

All 12 ACs pass; the flagship property suite is green, PR-blocking, deterministic, shrinking-enabled, and Docker-backed; the six/seven invariants (I1–I7) hold for every generated sequence; the balance trigger and RLS are proven the database second wall directly; period boundary inclusivity is pinned; no production code, migration, DDL, or frontend changed (any surfaced defect is a routed finding); FX/VAT rounding properties and load/perf are explicitly deferred. Then state → `verify` and route to LL QA Reviewer. On PASS, **Phase 2's exit criterion is met** — record it in status.md.

## Sequencing / blocker

**WP08 is the Phase-2 exit gate and cannot be completed before its two open dependencies land:**

| Blocker | Needed for | Current state |
|---|---|---|
| **P2-WP09** (idempotency middleware) | Invariant **I5** — "retried posts exactly-once" | **done** (PR #18 merged 2026-07-12) |
| **P2-WP10** (period create/open/close/lock) | Invariant **I3** — "closed-period posts rejected" | **done** (PR #19 merged 2026-07-12) |

All slices (I1–I7) are now implementable against `main` (WP02–WP10). The sequence **WP09 → WP10 → WP08** is complete through WP10; WP08 is the next implementation package and remains the Phase-2 exit gate.

## Risks / notes

- **Size is at the limit.** The generator + reference model + shrinking wiring is the real cost; the stack (factory, fixture, seeds, binding) is reused. If, in practice, the engine + all seven invariants exceed ≤ 2 days, split along a clean seam: **WP08a** = engine + reference model + I1/I2/I4/I6/I7 (against current `main`), **WP08b** = I3/I5 + the exit-gate declaration (after WP09/WP10). Record the split in status.md; the Phase-2 exit is only met when **both** are green. Prefer the whole WP if it fits.
- **Flakiness is unacceptable in a PR gate.** Bounded iteration counts, a fixed base seed per CI run (logged), `TZ=UTC`, and a fresh schema per property class (`LedgerDbFixture` collection) keep it deterministic. Any nondeterminism is a bug in the driver/model, not a reason to loosen the gate.
- **The reference model must not re-implement posting-validity.** It tracks *expected outcomes* (balances, live entries, period state) but delegates the *decision* to the system under test — otherwise the suite tests the model against itself. The WP01 fixtures already pin the rules; WP08 pins the *system composition*.
- **Time budget vs coverage.** PR runs a bounded budget; a deeper nightly budget (more iterations / longer sequences) is a carry-forward, not this WP. Keep the PR step fast enough to stay in the pipeline.
- **I7 may surface a real bug.** The WP05 `end_exclusive` vs inclusive `EndDate` flag means I7 could go red on first write. That is the suite doing its job — file a named finding for WP10/WP05 and (with user direction) fix in the owning WP; WP08 stays test-only.
- **Do not weaken the DB wall for the app wall.** I6 asserts the trigger + RLS *independently* of the application's eager checks (N3): both walls must hold. A green app-level property does not excuse a missing DB-level assertion.

## Implementation notes

*(none yet — planning only; populated by LL Backend Dev during implementation, after WP09 + WP10 are merged.)*
