# LeafLedger Rebuild — Status Tracker

> Master tracker. One row per work package. States: `planned | in-progress | verify | done | blocked`.
> Plan files: [plans/](./plans/). Specs: [docs/architecture/rebuild/](../architecture/rebuild/README.md). Playbook: [Part 7](../architecture/rebuild/07-vibe-coding-playbook.md).

## Phase 0 — Stabilize old system *(executed in the OLD repo `Lokkeccs/Accounting`)*

| WP | Title | State | Notes |
|---|---|---|---|
| P0-WP01 | Lock down /maintenance/* endpoints | done | Guard: Maintenance:Enabled flag (default OFF → 404) + AdminIdentities allowlist (403) + X-Maintenance-Confirm header (400). 8 unit tests. **Deployed to prod 2026-07-04** (health 200, endpoint 401/404). Old-repo commit pending. |
| P0-WP02 | Wire guard scripts into GitHub Actions (old repo) | planned | |
| P0-WP03 | Fix boundary violation (importUtils → data/fxConversion) | planned | |
| P0-WP04 | Verify/close export coverage (accounts, journal, master data) | planned | Self-migration path |
| P0-WP05 | Cosmos + blob backup; archive Accounting_docs | planned | |
| P0-WP06 | Usage probe: table rows per space (M1 vs M2 readiness) | planned | Sizes launch waves |

## Phase 1 — Repo + foundations

| WP | Title | State | Notes |
|---|---|---|---|
| P1-WP01 | Repo scaffold + CI skeleton (all gates blocking) | done | [plan](./plans/P1-WP01-repo-scaffold-ci.md) — QA PASS 2026-07-05; merged via PR #3 (all gates green), red-gate proven via PR #4 |
| P1-WP02 | docker-compose local dev (Postgres + API stub) | planned | |
| P1-WP03 | SharedKernel: Money, Ids, Period, Result | planned | |
| P1-WP04 | OpenAPI → generated TS client pipeline | planned | |
| P1-WP05 | ADR log + ADR-001 (online-first + Postgres + no-migration) | planned | |

## Phase 2 — Ledger core
*(WPs to be planned by LL Architect after P1-WP03; fixtures first — see playbook §5)*

## Phase 3 — Frontend re-platform
## Phase 4 — Feature porting (M1 → M2)
## Phase 5 — Integrations
## Phase 6 — QA hardening
## Phase 7 — Launch & sunset

---

## Session log
| Date | WP | Agent | Result |
|---|---|---|---|
| 2026-07-03 | — | (seeding) | Repo cloned; specs, agents, instructions, tracker, P1-WP01 plan seeded |
| 2026-07-04 | P1-WP01 | LL Backend Dev | Backend scaffold: sln, Directory.Build.props, SharedKernel (empty), Host `/health` (200 verified), arch tests 3/3 green |
| 2026-07-04 | P1-WP01 | LL Frontend Dev | Frontend scaffold, boundary ESLint, vitest, page-budget tool, workflows, root hygiene; all local gates green; boundary rules verified to fail |
| 2026-07-05 | P1-WP01 | LL QA Reviewer | PASS → done: verified on merged main (backend 3/3, frontend lint/typecheck/test/budget green); PR #3 CI all-green (criterion 3); PR #4 red-gate proven (criterion 4) |
