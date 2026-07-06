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
| P1-WP02 | docker-compose local dev (Postgres + API stub) | done | [plan](./plans/P1-WP02-docker-compose-local-dev.md) — QA PASS 2026-07-06; **merged to main via PR #5** (CI green). |
| P1-WP03 | SharedKernel: Money, Ids, Period, Result | done | [plan](./plans/P1-WP03-sharedkernel-value-types.md) — QA **PASS** 2026-07-06 (F1 fixed). Release build 0/0; 45/45 unit + 3/3 arch. [ADR-0002](./../architecture/adr/ADR-0002-id-storage.md). **Merged to main via PR #6.** |
| P1-WP04 | OpenAPI → generated TS client pipeline | done | [plan](./plans/P1-WP04-openapi-client-pipeline.md) — QA **PASS** 2026-07-06. Build-time OpenAPI doc → committed `backend/openapi/leafledger-v1.json`; `openapi-typescript`+`openapi-fetch` → `app/src/api/`; `getMeta()` + vitest; CI `contract` diff gate (red-gate independently reproduced). Release 0/0; arch 3/3; lint/typecheck/test/page-budget green; 0 vulns. **Merged to main via PR #8** (`24c21df`; rebased onto main after #6 squash-merge to clear stacked-branch conflicts). |
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
| 2026-07-06 | P1-WP02 | LL Git | Branch + commit `7cc83ac` + draft PR #5. User confirmed CI green and **merged to main**. |
| 2026-07-06 | P1-WP03 | LL Architect | Plan drafted: SharedKernel value types (Money/minor-units, ULID`Id<T>`↔uuid per N1, Period, Result). Greenfield rewrite (no golden fixtures; behavioral porting is Phase 2). ADR for ID storage. 2 open questions routed to LL Accounting Expert. Awaiting approval. |
| 2026-07-06 | P1-WP03 | LL Accounting Expert | Resolved open questions: (A) decimal→minor rounding = `AwayFromZero` (Swiss commercial rounding, per OR/ESTV practice; explicit API, no hidden default; legal VAT/FX rounding stays Phase 2); (B) `Period` = plain half-open `[start, endExclusive)` range + status (OR art. 958 flexibility met by the range primitive; fiscal-calendar derivation is Phase 2). Answers recorded in plan; ACs #3/#10 tightened. |
| 2026-07-06 | P1-WP03 | LL Backend Dev | Implemented Money/CurrencyCode/Id<T>/Period/Result + ADR-0002. Release build 0/0; 44/44 unit tests; 3/3 arch tests. In-house big-endian ULID↔uuid conversion (order-preserving, property-tested); `Ulid` 1.3.4 dep added to SharedKernel. Deviations: generative xUnit instead of CsCheck; `Error`→`DomainError` (CA1716); CA1000 suppressed on generic value types; CA1707 NoWarn in test project. → verify. |
| 2026-07-06 | P1-WP03 | LL QA Reviewer | FAIL → in-progress. Independently reproduced: Release 0/0, 44/44 unit + 3/3 arch, integration excluded, no scope creep. F1 (AC-4): no-float reflection test covers only `Money`, not `CurrencyCode` as the AC mandates (invariant holds; test-coverage gap). All other ACs + financial-integrity/security/hallucination/patch-layering checks PASS. |
| 2026-07-06 | P1-WP03 | LL Backend Dev | Fixed F1: `No_public_member_exposes_float_or_double` converted from `[Fact]` to `[Theory]` over `Money` and `CurrencyCode`. Release build 0/0; 45/45 unit + 3/3 arch pass; integration excluded. → verify. |
| 2026-07-06 | P1-WP03 | LL QA Reviewer | **PASS** (re-verify). F1 closed — no-float theory now covers `Money` + `CurrencyCode`. Independently reproduced: Release 0/0, 45/45 unit + 3/3 arch, integration excluded, no scope creep. All ACs met. → done. Ready for LL Git (exclude 3 unrelated agent-file deletions). |
| 2026-07-06 | P1-WP04 | LL Architect | Plan drafted: single-contract pipeline. Backend build-time OpenAPI doc (`Microsoft.AspNetCore.OpenApi` + `Microsoft.Extensions.ApiDescription.Server`) → committed `backend/openapi/leafledger-v1.json`; `/api/v1/meta` anchor endpoint; frontend `openapi-typescript`+`openapi-fetch` → `app/src/api/` (types + stable `client.ts`); application-layer `getMeta()` wrapper + vitest; CI `contract` diff gate (drift-killer) with red-gate proof. No golden fixtures / no accounting decision (pure tooling). Tool choice flagged as candidate ADR-0003 for P1-WP05. Awaiting approval. |
| 2026-07-06 | P1-WP04 | LL Backend Dev | Backend half implemented: Host OpenAPI packages + build-time doc emission → committed deterministic `backend/openapi/leafledger-v1.json` (byte-identical across `--no-incremental` regens); `/api/v1/meta` → `MetaResponse{name,version}` (both required strings); doc transformer sets `info.title/version=v1`; filename normalized via MSBuild `Move` target (tool has no filename knob). Release build 0/0; arch 3/3. → frontend gen + CI gate next. |
| 2026-07-06 | P1-WP04 | LL Frontend Dev | Frontend half implemented: `openapi-typescript`+`openapi-fetch` → `app/src/api/{schema.d.ts,client.ts}`; `gen:api` script; application-layer `getMeta()` + vitest (map + throw); CI `contract` diff gate in pr.yml/main.yml (build → gen → `git add -N` + `git diff --exit-code`), wired into main deploy `needs`. **Red-gate proven**: drift exit 1 (both artifacts) / restored exit 0 (deterministic). Fixes: `.npmrc legacy-peer-deps` (tool peer TS^5 vs repo TS6), `.gitattributes` LF pin. Gates green: lint/typecheck/test(3)/page-budget; npm audit 0 vulns; dotnet vulnerable none. → verify. |
| 2026-07-06 | P1-WP04 | LL QA Reviewer | **PASS → done.** Independently reproduced all 10 ACs: Release 0/0, arch 3/3, contract `GET /api/v1/meta`+`MetaResponse{name,version}`/`info.version=v1`, `schema_fresh=True`, vitest 3/3, lint/typecheck/page-budget green, npm audit 0 / dotnet vulnerable none. Red-gate independently reproduced (source mutation changes both artifacts; restore byte-identical). Security: OpenAPI doc Dev-only, no secrets, no-input anchor. Non-blocking notes: N1 `.gitattributes`/`.npmrc` outside file-list (documented deviations), N2 branch hygiene for LL Git, N3 meta endpoint unauth (acceptable, auth is Non-goal). Cleared for LL Git (branch P1-WP04; 16-path set). |
| 2026-07-06 | P1-WP03 | LL Git | Branch `P1-WP03-sharedkernel-value-types` + PR #6 **squash-merged to main** (`f5e9fa5`). |
| 2026-07-06 | P1-WP04 | LL Git | Committed 16-path set to `P1-WP04-openapi-client-pipeline` (`dcaa253`), pushed, opened PR #7 (stacked on P1-WP03). After #6 squash-merged, stacked PR #8 (base main) showed conflicts (`Period.cs`, `status.md`); rebased branch `--onto origin/main` (dropped redundant P1-WP03 commits → single commit `8123de3`), force-pushed with lease → PR #8 CLEAN/MERGEABLE. #7 had already merged into the orphaned P1-WP03 branch (harmless). **PR #8 squash-merged to main** (`24c21df`). |
