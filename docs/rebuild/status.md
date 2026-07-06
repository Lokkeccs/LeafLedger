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
| P1-WP02 | docker-compose local dev (Postgres + API stub) | done | [plan](./plans/P1-WP02-docker-compose-local-dev.md) — QA PASS 2026-07-06 (F1/F2 fixed & re-verified: filtered backend test 3/3 exit 0, Host-only restore exit 0, Release 0/0). AC1/2/3/6 (Docker) CI-proven; merge on green CI. |
| P1-WP03 | SharedKernel: Money, Ids, Period, Result | planned | Must include ID-storage decision ([risk-review N1](./plans/NOTES-risk-review-2026-07-06.md)) |
| P1-WP04 | OpenAPI → generated TS client pipeline | planned | |
| P1-WP05 | ADR log + ADR-001 (online-first + Postgres + no-migration) | planned | + ADR for ID storage from P1-WP03 (risk-review N1) |

## Phase 2 — Ledger core
*(WPs to be planned by LL Architect after P1-WP03; fixtures first — see playbook §5. Must fold in [risk-review notes](./plans/NOTES-risk-review-2026-07-06.md) N2 (idempotency lifecycle), N3 (eager balance check), N4 (mat-view refresh).)*

## Phase 3 — Frontend re-platform
*(Must fold in [risk-review notes](./plans/NOTES-risk-review-2026-07-06.md) N5 (SignalR coalescing), N6 (invalidation map).)*

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
| 2026-07-05 | P1-WP02 | LL Architect | Plan drafted: docker-compose (Postgres+API stub), DB readiness health check, Testcontainers integration pattern (main-branch gate); awaiting approval |
| 2026-07-06 | — | LL Architect | External architecture risk review triaged with user: 6 items adopted (N1–N6), 3 deferred, rest already covered/rejected → [plans/NOTES-risk-review-2026-07-06.md](./plans/NOTES-risk-review-2026-07-06.md); WP rows annotated |
| 2026-07-06 | P1-WP02 | LL Backend Dev | Implemented compose/Dockerfile/health/integration/CI/README; Release build clean, arch 3/3; integration fails locally (no Docker). Moved to verify. |
| 2026-07-06 | P1-WP02 | LL QA Reviewer | FAIL → in-progress: F1 solution-wide `dotnet test` runs Testcontainers on PRs (violates non-goal); F2 Dockerfile `dotnet restore sln` fails (MSB3202, missing test csproj → image won't build). Both reproduced locally. Build Release clean; arch 3/3. |
| 2026-07-06 | P1-WP02 | LL Backend Dev | Fixed F1 (`[Trait("Category","Integration")]` + `--filter "Category!=Integration"` on backend job in pr.yml/main.yml) and F2 (Dockerfile restores Host csproj only). Verified locally: filtered backend test 3/3 exit 0; Host-only restore exit 0. → verify. |
| 2026-07-06 | P1-WP02 | LL QA Reviewer | PASS → done (conditional on CI): F1/F2 re-verified — filtered backend test 3/3 exit 0 (integration excluded), Host-only restore exit 0, Release 0/0. AC1/2/3/6 (Docker runtime) CI-proven; no local Docker engine. Merge only on green CI. |
