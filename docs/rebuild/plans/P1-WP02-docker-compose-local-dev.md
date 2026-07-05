# P1-WP02 — docker-compose local dev (Postgres + API stub)

State: planned (awaiting user approval)          Last session: 2026-07-05

## Goal
One-command local onboarding — `docker compose up` starts Postgres + the API and they are wired together — plus the Testcontainers integration-test pattern established for later phases. No schema, no domain, no EF yet.

## Scope
1. **`docker-compose.yml`** (repo root): two services —
   - `db`: `postgres:17` (pinned), named volume for data, healthcheck (`pg_isready`), port `5432` mapped, credentials from env.
   - `api`: built from the Host Dockerfile, depends_on `db` (condition: service_healthy), `ConnectionStrings__Postgres` injected via env, port `8080` mapped.
2. **API Dockerfile** (`backend/src/LeafLedger.Host/Dockerfile`): multi-stage (SDK build → `aspnet:9.0` runtime), non-root user, `EXPOSE 8080`. `backend/.dockerignore` to keep context small (no `bin/obj/node_modules`).
3. **DB readiness in the Host** (thin, no EF): add a Postgres **readiness** health check (`AspNetCore.HealthChecks.NpgSql`, `SELECT 1`) tagged `ready`.
   - `GET /health` stays **liveness** (self only, no DB) → always 200 when the process is up.
   - `GET /health/ready` = **readiness** (DB reachable) → 200 when Postgres answers, 503 when not.
   - Connection string comes from configuration/env (`ConnectionStrings:Postgres`); **no hardcoded credentials**; `appsettings.Development.json` holds only a localhost placeholder overridden by compose.
4. **`LeafLedger.IntegrationTests`** (new xunit project): `Testcontainers.PostgreSql` spins a throwaway Postgres, opens an Npgsql connection, asserts `SELECT 1` = 1. Establishes the integration-test pattern (Part 4 risk #6). Added to the solution.
5. **CI**: wire the integration test into **`main.yml`** only (a new `integration` job) — per Part 3 §10, Testcontainers runs on the main branch, not on PRs (keeps PR fast). PR workflow is unchanged.
6. **`.env.example`** (root, committed) documenting `POSTGRES_USER/PASSWORD/DB`; real `.env` stays gitignored. **README** gains a "Local development" section (prereq: Docker Desktop; `docker compose up`; the two health URLs; `docker compose down -v`).

## Non-goals (explicitly deferred)
- No EF Core, `DbContext`, migrations, or SQL schema — that is **P2** (ledger core). The `db` starts empty.
- No RLS, audit triggers, balance triggers (P2).
- No SharedKernel domain types (Money/Ids/Period/Result) — **P1-WP03**.
- No OpenAPI/TS-client pipeline — **P1-WP04**.
- No Vercel/Azure/staging deploy wiring; no SignalR; no auth pipeline.
- No Testcontainers gate on the **PR** workflow (main-branch only per §10).

## Source material
- Specs: Part 3 §10 (local dev `docker compose up`; Testcontainers = main gate), §9 (health/readiness, observability), Part 4 Phase 1 + Risk #6 ("Testcontainers integration tests from P1").
- Old repo (`https://github.com/Lokkeccs/Accounting`): **nothing to port** — the old stack was Cosmos/Dexie with no compose or Postgres. Recorded so the porter/QA don't look for a source.
- Current code: `backend/src/LeafLedger.Host/Program.cs` (already `MapHealthChecks("/health")`), `LeafLedger.Host.csproj`, `backend/LeafLedger.sln`, `.github/workflows/main.yml`, root `README.md`.

## Golden fixtures
N/A — this WP contains no accounting behavior. No fixtures required; no LL Accounting Expert consultation needed.

## File list (expected)
- `docker-compose.yml` (new, root)
- `.env.example` (new, root)
- `backend/src/LeafLedger.Host/Dockerfile` (new)
- `backend/.dockerignore` (new)
- `backend/src/LeafLedger.Host/Program.cs` (modify — add readiness health check + `/health/ready`)
- `backend/src/LeafLedger.Host/LeafLedger.Host.csproj` (modify — add `AspNetCore.HealthChecks.NpgSql`)
- `backend/src/LeafLedger.Host/appsettings.json` + `appsettings.Development.json` (connection-string placeholder, env-overridden)
- `backend/tests/LeafLedger.IntegrationTests/**` (new project: `Testcontainers.PostgreSql`, `Npgsql`, xunit)
- `backend/LeafLedger.sln` (modify — add integration project)
- `.github/workflows/main.yml` (modify — add `integration` job)
- `README.md` (modify — Local development section)

## Acceptance criteria (concrete tests)
1. **Compose comes up wired:** `docker compose up -d --build` → both services running; `db` reaches `healthy`; `curl -f http://localhost:8080/health` → 200 `Healthy`; `curl -f http://localhost:8080/health/ready` → 200 within a bounded wait (api `depends_on` db healthy).
2. **Readiness actually checks Postgres:** with the stack up, `docker compose stop db` → `GET /health/ready` returns **503** while `GET /health` still returns **200**. (Proves the readiness probe is real, not cosmetic.)
3. **Integration test green:** `dotnet test backend/tests/LeafLedger.IntegrationTests` passes locally — Testcontainers Postgres starts, `SELECT 1` returns 1, container disposed. The same job passes in `main.yml`.
4. **Config validates & is clean:** `docker compose config` exits 0; `docker compose down -v` removes the volume; `.env` is gitignored; `.env.example` is committed; `gitleaks`/secret grep find no credentials in tracked files.
5. **No regression:** `dotnet build` (Release, warnings-as-errors) and `dotnet test` for existing projects stay green; NetArchTest still 3/3; no new analyzer warnings.
6. **Onboarding documented:** README "Local development" section lets a fresh clone go from `docker compose up` to a 200 on `/health/ready` following only the documented steps.

## Manual steps for the user (not agent work)
- Install/enable **Docker Desktop** (or a Docker engine) locally to run/verify compose.
- No cloud resources — this WP is fully local + GitHub-hosted CI.

## Risks / open questions
- **Testcontainers in CI:** GitHub `ubuntu-latest` runners include Docker, so the `integration` job runs without extra setup; if flakiness appears, cap with a startup timeout and retry-once — record any such tuning in notes.
- **Postgres image tag:** pin `postgres:17` (major) for reproducibility; Renovate bumps later. No `latest`.
- **Port clashes:** 5432/8080 may be taken on a dev machine; document overriding via `.env`/compose `ports` in the README.

## Implementation notes
*(filled by LL Backend Dev during implementation)*

## QA verdict
*(filled by LL QA Reviewer only)*
