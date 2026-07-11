# ADR-0005: Posting requires a defined, open period — deliberate divergence from the old "no period ⇒ allowed" behavior

- **Status:** accepted
- **Date:** 2026-07-11
- **Deciders:** LL Accounting Expert (rule owner), LL Architect (planned), LL Docs Editor (recording)
- **Related:** WP P2-WP04 ([plan](../../rebuild/plans/P2-WP04-ledger-domain-journal-entry.md)); pinned by P2-WP01 golden fixtures ([plan](../../rebuild/plans/P2-WP01-golden-fixtures-posting-rules.md)); target architecture §3/§4 (Ledger owns periods; posting validity); Swiss Code of Obligations **Art. 957a(2)**, **Art. 958f**; **GeBüV Art. 3**

## Context
The old system (`Lokkeccs/Accounting`, `postingValidity.ts` / `periodUtils.ts`) evaluated the period
state of a posting date and **allowed** a posting when **no period was defined** for that date
(`no-period-defined ⇒ allowed`), rejecting only `closed`/`locked` periods with a `PeriodClosedError`.
This backward-compatible permissiveness is captured verbatim in the P2-WP01 golden fixtures — one
`period-state` / `assertPostingPeriodOpen` case asserts `ok` for a date with no defined period.

The golden fixtures are the porting **oracle**: the default rule of the rebuild is to reproduce old
behavior exactly, and any divergence must be recorded deliberately rather than smuggled into code.
P2-WP04 (Ledger domain) is the WP that ports posting-validity and period-state, so the decision on
whether to keep or change this rule had to be settled at P2-WP04 planning. LL Accounting Expert was
consulted (question A1).

The forces:
- **Completeness / no unassigned postings.** Swiss statutory bookkeeping requires complete, truthful,
  systematic and verifiable records (OR 957a(2)) with an auditable trail (OR 958f; GeBüV Art. 3). A
  posting that belongs to *no* period is an unassigned record that weakens period-close controls and
  makes "which periods are final" non-deterministic.
- **Determinism of close controls.** If a date can post with no period, closing a period never fully
  guarantees that later postings cannot appear in an unmanaged gap.
- **Backward compatibility.** The old permissive behavior is real captured behavior; changing it is a
  genuine divergence from the oracle, not a bug fix, and must be traceable.

Swiss law does not prescribe LeafLedger's period-master data model; requiring an open period is an
**internal-control product policy** that *implements* the OR/GeBüV principles above, not a statutory
data-model mandate.

## Decision
We will **require every new posting to resolve to a defined period whose effective state is `open`.**

- A posting date that maps to `no-period-defined`, `closed`, or `locked` is **rejected**.
- `GetEffectivePeriodState(txDate, periods)` still returns `no-period-defined` when no period contains
  the date — the period-state read is unchanged. The **posting-validation** step is what rejects that
  result, with a **distinct stable error code** (for example `posting_period.not_defined`) rather than
  masquerading as a `PeriodClosedError` for a non-existent named period.
- **The old golden fixture is preserved unchanged.** The P2-WP04 fixture consumer treats the
  `no-period-defined ⇒ ok` case as the single documented **OLD→target divergence**: the test identifies
  the old expected `ok` and then proves the new target rejection, so history stays immutable while the
  target rule is pinned by an explicit test that links back to this ADR.
- **Reversals are subject to the same guard** (see ADR follow-up in P2-WP04 A2): a reversal carries an
  explicit effective date and must also land in a defined, open period.
- **Bootstrap/onboarding must create an open period before the first posting.** Wiring this into the
  onboarding/posting application flow is P2-WP05 (not the pure domain WP04).

Period-overlap prevention (whether two periods may cover the same date) is **out of scope** for this
decision and must not be invented in P2-WP04.

## Consequences
- **Positive:** No unassigned postings; deterministic period-close controls; a single, explicit stable
  error for "no period defined"; stronger alignment with OR 957a(2)/958f and GeBüV Art. 3.
- **Positive:** The divergence from the oracle is explicit and test-pinned, so the rebuild's
  "reproduce old behavior unless deliberately changed" contract is upheld.
- **Neutral:** `GetEffectivePeriodState` semantics are unchanged; only posting validation gains the
  stricter gate. Reversals inherit the same rule.
- **Negative / follow-up:** Onboarding and any programmatic first-posting flow must ensure a defined,
  open period exists first, or postings fail — a new precondition to satisfy in **P2-WP05** (posting/
  reversal/period application), including period create/open/close/lock management. The old permissive
  path is no longer available as a fallback.

## Alternatives considered
- **Keep the old behavior (`no-period-defined ⇒ allowed`).** Rejected: permits unassigned postings and
  makes close controls non-deterministic; weaker against OR/GeBüV completeness and auditability
  principles. It would, however, have required no fixture divergence.
- **Auto-create a period on demand at posting time.** Rejected for this WP: hides a control decision
  (which fiscal period a posting belongs to) inside the posting path; period lifecycle is an explicit,
  authorized action owned by P2-WP05, not a silent side effect.
- **Reuse `PeriodClosedError` for the no-period case.** Rejected: conflates "a named period is closed"
  with "no period exists," losing a distinct, actionable error and misreporting the period name/state
  payload. A dedicated code (`posting_period.not_defined`) is clearer for callers and QA.
