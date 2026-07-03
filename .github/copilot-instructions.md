# LeafLedger Rebuild — Repository Instructions (all agents)

## Context
Greenfield rebuild of an accounting system. Authoritative specs: `docs/architecture/rebuild/` (parts 01–07).
Work is organized as work packages (WPs) under `docs/rebuild/plans/`; master tracker: `docs/rebuild/status.md`.
The OLD codebase (`Lokkeccs/Accounting`, locally `C:\Programming\LeafLedger\Accounting`) is read-only reference material — behavior oracle, never a style guide.

## Non-negotiable domain rules
1. Money = integer minor units + ISO currency. No float arithmetic on amounts, ever.
2. Debits must equal credits; the server and the database enforce it — client checks are UX only.
3. Posted journal entries are immutable; corrections are reversals.
4. Every write endpoint is idempotent (idempotency key) and authorized (space role + license).
5. Never invent accounting behavior: it comes from the WP plan, the target spec, or the old code — traceably. If unsure, stop and ask.

## Session protocol
- Start: read the WP plan file + `docs/rebuild/status.md`. If no approved plan exists for the task, refuse and route to LL Architect.
- One work package per session. Scope creep goes to plan notes as proposed WPs, never into code.
- Plan before edit; smallest viable diff; tests define done.
- End: update the plan file (dated note, state) and `status.md`. Report: what changed, actual test results, next step.

## Safety
- Never run destructive commands (data deletion, force push, resets) without explicit user confirmation.
- Never commit or push unless the user asks in the current conversation (LL Git handles it).
- No secrets in code, config, or docs. No new dependencies without a plan-file entry.
- Do not edit generated files (`app/src/api/**`); regenerate instead.

## Quality gates (all must pass before a WP moves to "verify")
Lint, typecheck, boundary/architecture tests, page budgets, and the unit + financial-invariant tests relevant to the WP.
A PR is created only from a WP in "done" state with a PASS QA verdict.

## Communication
Concise, neutral, no filler. Calibrated confidence ("likely", "uncertain"). Diff-oriented output.
Every response ends with: current WP state + exactly one next action.
