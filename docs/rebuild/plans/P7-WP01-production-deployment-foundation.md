# P7-WP01 — Production deployment foundation (Azure Static Web Apps + Azure API + Azure PostgreSQL + SignalR + Entra + secrets + migrations + deploy gates)

- **Phase:** 7 (Launch & sunset) — the **launch-enabling infrastructure** WP. It converts the current *local/Docker-only* system into a *hosted production* system: the SPA on Azure Static Web Apps, the .NET Host on Azure, PostgreSQL on Azure, Azure SignalR for live invalidation, production Entra, secret management, a production migration path, and the real deploy pipeline gates. **May proceed in parallel with Phase 4** (it touches only deployment config/infra, not feature code); it does not block feature WPs and feature WPs do not block it.
- **State:** planned — **all nine front-loaded decisions APPROVED by the user 2026-07-17**; now **awaiting only user provisioning of the cloud resources** (Azure Static Web Apps resource, Azure App Service for Containers, Azure PostgreSQL Flexible Server, Azure SignalR Service, production Entra SPA + API app registrations) and secret entry. LL Architect cannot create cloud resources or hold secrets; this plan defines exactly what must be provisioned and wired, and by whom. Implementation of each sub-WP starts as its prerequisite resources come online.
- **Owner (implementation):** split — **LL Frontend Dev** (Azure Static Web Apps config), **LL Backend Dev** (Host production config + migration step + deploy jobs), and **the user / an ops operator** (cloud resource provisioning, Entra registration, secret entry — actions an agent must not and cannot perform). Every code/config edit is routed to the named Dev agent; every portal/secret action is a user step, explicitly labelled below.
- **Estimated size:** **exceeds ≤2 days as a single unit** — decomposed into **five independently-verifiable sub-WPs** (P7-WP01a…e), each ≤2 days, with a documented split seam. Do **not** implement as one session.
- **Depends on:**
  - **P1-WP02** (done) — the `docker-compose` stack + the Host `Dockerfile` (`backend/src/LeafLedger.Host/Dockerfile`, non-root, `EXPOSE 8080`) — the exact image deployed to Azure.
  - **P2-WP02** (done) — the EF `InitialLedgerSchema` migration + all later Ledger migrations — the production migration payload.
  - **P2-WP11 / P2-WP13** (done) — Entra token validation (JWKS + audience + issuer/tenant allowlist) + identity-links — the production authN the Entra app registration must satisfy (`appsettings.json` `Authentication.Authority` = `login.microsoftonline.com/common/v2.0`, `Audiences` include the client-id GUID, `RequiredScope=ledger.write`).
  - **P3-WP02** (done) — MSAL SPA auth; production needs a real SPA app registration + `VITE_MSAL_*` values + redirect URIs pointing at the Azure Static Web Apps domain.
  - **P3-WP08** (done) — the Azure SignalR **config seam** already in `Program.cs` (`ConnectionStrings:AzureSignalR` → `.AddAzureSignalR(...)`); this WP provisions the resource and supplies the connection string. No code change to the seam.
  - **P3-WP09** (done) — the `e2e-smoke` main-blocking job already gating the placeholder deploys — the gate this WP extends into a real staged deploy.
- **Blocks:** the M1 **launch** (strategy 7.2 — launch after M1). Without this WP the app runs only locally; the production frontend host shows nothing because no production frontend build, no production API origin, and no hosted database exist yet.

## Context / scope note (LL Architect)

**Why "nothing is shown in production" is the expected current state, not a bug.** (The user's earlier Vercel trial and the newly-chosen Azure Static Web Apps target both show nothing for the same reason.) The repository today is deliberately local/Docker-only:

- `.github/workflows/main.yml` still has **placeholder** `deploy-api` (*"No-op — real API deploy wired in P2/P3."*) and `deploy-frontend` (*"No-op — real Vercel deploy wired in P3."*) jobs (the placeholder comment predates the Azure Static Web Apps decision and will be replaced in WP01e). No production deploy has ever run.
- `app/vite.config.ts` proxies `/api` and `/hubs` **only to `http://localhost:8080`** (dev + preview) — those proxies do not exist in production (neither on Vercel nor on Azure Static Web Apps).
- `app/src/api/client.ts` uses same-origin `baseUrl = '/'` — a statically hosted SPA has no API at its own origin unless a rewrite / linked backend or an absolute base URL is configured.
- `docker-compose.yml` PostgreSQL is local dev infrastructure; the Host only runs migrations + `DevSeed` **when `ASPNETCORE_ENVIRONMENT=Development`** (`Program.cs`).
- Azure SignalR is a config seam only; the resource is unprovisioned (a documented P3-WP08 carry-forward).

So the completed Phase-1→Phase-3 functionality is intact locally; **production hosting was simply never built.** This WP builds it. It is pure infrastructure/config — **no accounting behavior, no financial value, no domain change** — therefore **no golden fixtures and no LL Accounting Expert consult** (see those sections).

**One load-bearing functional gap this WP surfaces (not solves):** even with infra deployed, a first real Entra user has **no space and no data** — `DevSeed` is Development-only, and there is **no production onboarding path yet** (space creation + membership + a starting chart of accounts are later feature WPs: CoA write = P4-WP05, admin/invitations = P4-WP18). A signed-in production user will therefore hit `403 auth.not_a_member` against an empty database. **Production is not *usable* until a minimal onboarding path exists** — this is recorded as a hard **dependency / open question** below and must be resolved (its own WP) before a public launch, though the infra in this WP can be stood up and health-verified first.

## Spec sources

- `docs/architecture/rebuild/03-target-architecture.md` §1 (the target topology: modular-monolith Host + PostgreSQL + Azure SignalR Service + Azure Blob + Azure AI as side services), §2 (Cosmos → **PostgreSQL**; self-hosted SignalR → **Azure SignalR Service**), §8 (Entra `common`; authZ pipeline).
- `docs/architecture/rebuild/04-implementation-plan.md` §Phase 7 (launch & sunset; fresh-start / no data migration — ADR-0001), §7 (launch strategy 7.2 — launch after M1).
- `docs/architecture/rebuild/05-quality-and-maintainability.md` §1.2 (**the deploy pipeline: main pipeline → integration + E2E smoke → deploy staging slot → `/health` + `/integrity` probe → swap**; weekly full E2E/load) — the exact gate flow P7-WP01e wires.
- `docs/architecture/rebuild/01-current-architecture.md` §Backend (the OLD stack ran on **Azure App Service (Linux, B1)** — continuity reference for the API host choice) and `02-weaknesses.md` §9 (build/deploy issues — what not to repeat).
- `docs/architecture/adr/ADR-0001-online-first-postgres.md` (online-first + PostgreSQL system-of-record + **fresh-start launch, no bulk data migration** — the production DB starts empty).
- `docs/rebuild/plans/P3-WP08-signalr-live-invalidation.md` (the Azure SignalR **Default-mode config seam** + the *"Azure SignalR provisioning (ops)"* carry-forward this WP discharges).
- `docs/rebuild/plans/P3-WP09-playwright-e2e-two-browser-smoke.md` (the `e2e-smoke` main-blocking job + `docker-compose.e2e.yml` — the gate this WP extends; the E2E auth seam that must stay Development-only in production).
- `.github/instructions/backend.instructions.md` (money/invariants unchanged; no secrets in code; expand-contract migrations) and `.github/instructions/frontend.instructions.md` (generated client is the only REST access point; no direct fetch).

## Goal

The application is reachable at a public Azure Static Web Apps URL, signs users in via production Entra, talks to a hosted .NET API over HTTPS, persists to Azure PostgreSQL, live-updates via Azure SignalR, and deploys through a gated `main` pipeline (build → tests → E2E smoke → **staged deploy** → `/health` + `/integrity` probe → **promote**), with **all secrets held outside the repo**. Concretely:

1. **Frontend (Azure Static Web Apps):** a production SPA build served on Azure Static Web Apps, with a SPA history fallback (`navigationFallback`) and a routing layer (`staticwebapp.config.json` routes / a linked backend) that sends `/api/*` and `/hubs/*` to the Azure API origin (rewrite / linked backend or configured base URL — D-DEPLOY-API-BASEURL), and production `VITE_*` values (MSAL, API scope, demo/space config, **`VITE_E2E_AUTH` absent**).
2. **API (Azure):** the existing Host container deployed to an Azure compute host (D-DEPLOY-API-HOST) with production configuration (connection string, Entra audience/authority/tenant allowlist, `AllowedHosts`, CORS if the base-URL route is chosen), a platform health-check bound to `/health/ready`, and **no** Development-only behavior active (no `DevSeed`, no `MapOpenApi`, no E2E scheme).
3. **Database (Azure):** an Azure Database for PostgreSQL Flexible Server (version matching the local `postgres:17`), TLS-required, network-restricted to the API, its connection string held as a secret, and the schema applied via a **deploy-time migration step** (not app-startup — the Host migrates only in Development).
4. **Realtime (Azure SignalR):** an Azure SignalR Service (Default mode) provisioned; its connection string supplied to the Host as `ConnectionStrings:AzureSignalR` so the existing seam activates with no code change; WebSocket transport reachable through the frontend routing layer.
5. **Identity (Entra):** production SPA + API app registrations (redirect URIs = the Azure Static Web Apps domain(s); API exposing `ledger.write`/`ledger.read` scopes; audience = the configured client-id GUID; tenant policy per `common`), with the values wired as `VITE_MSAL_*` (frontend) and `Authentication:*` (backend).
6. **Secrets:** every credential (DB password/connection string, SignalR connection string, any API keys) held in GitHub Actions secrets and/or Azure Key Vault / platform app settings — **never committed**; the `gitleaks` job stays green; `.env.example` documents variable **names** only.
7. **Deploy gates:** the placeholder `deploy-api` / `deploy-frontend` jobs replaced by real, **gated, staged** deploys following Part 5 §1.2 (deploy to a staging slot/preview → probe `/health` + `/integrity` → promote/swap on green; fail-closed otherwise), keeping the existing PR gates untouched.

## Scope — decomposed into five ≤2-day sub-WPs

> Each sub-WP is independently verifiable. Implementation order is **P7-WP01c (DB) → P7-WP01b (API) → P7-WP01d (SignalR + Entra + secrets) → P7-WP01a (Static Web Apps) → P7-WP01e (gates)**, because the frontend routing target needs the API origin, which needs the DB. Provisioning (portal) steps are the user's; code/config edits are the named Dev agent's.

### P7-WP01a — Frontend production deploy (Azure Static Web Apps) — *LL Frontend Dev + user (Azure Static Web Apps resource)*
- **Config (LL Frontend Dev):** a `staticwebapp.config.json` (app root `app/`) with: a `navigationFallback` to `/index.html` (excluding hashed assets) for the **SPA history fallback**, and `routes` that forward `/api/*` and `/hubs/*` to the Azure API origin via a SWA **linked backend** or a `rewrite` to the API host (D-DEPLOY-API-BASEURL); the SWA build uses `app_location: app`, `output_location: dist`, build command `npm run build`. **Realtime note:** with Azure SignalR in **Default mode** only the `/hubs/*/negotiate` HTTP call is proxied to the API — the actual WebSocket connects **directly** to the Azure SignalR service endpoint, not through the static host, so no WebSocket needs to traverse SWA. If the absolute-base-URL route is chosen instead, add a `VITE_API_BASE_URL` seam consumed by `app/src/api/client.ts` (a small, reviewed change to the currently-`'/'` `baseUrl`) and the SignalR hub URL builder — **this is the only application-code touch in this sub-WP and must be traced to D-DEPLOY-API-BASEURL**.
- **Env (user, in the SWA build / GitHub Action):** production `VITE_MSAL_CLIENT_ID`, `VITE_MSAL_AUTHORITY`, `VITE_MSAL_REDIRECT_URI` (the SWA URL), `VITE_API_SCOPE`, `VITE_DEMO_SPACE_ID` / `VITE_DEMO_BASE_CURRENCY` (or their production successors), and **`VITE_E2E_AUTH` unset**. (Build-time `VITE_*` values are supplied to the SWA build step / `Azure/static-web-apps-deploy` action.)
- **Deploy (user + LL Backend Dev):** the `Azure/static-web-apps-deploy` GitHub Action in the `deploy-frontend` job, authenticated with the SWA deployment token (secret); SWA's built-in per-branch/PR **preview environments** provide the frontend staging surface for D-DEPLOY-GATES.

### P7-WP01b — API hosting (Azure) + production configuration — *LL Backend Dev + user (Azure resource)*
- **Provision (user):** the chosen Azure compute host (D-DEPLOY-API-HOST) running the existing Host container image; a platform **health probe** bound to `/health/ready`; HTTPS-only; the region matching the DB.
- **Config (LL Backend Dev, via environment/app settings — never committed secrets):** `ConnectionStrings__Postgres` (from WP01c, as a secret ref), `Authentication__Authority` / `Authentication__Audiences` / `Authentication__TenantAllowlist` / `Authentication__RequiredScope` (from WP01d), `AllowedHosts` set to the API domain, and **CORS** for the Azure Static Web Apps origin **iff** D-DEPLOY-API-BASEURL chose the absolute-base-URL route (not needed if the same-origin linked-backend/rewrite route is used). Confirm `ASPNETCORE_ENVIRONMENT=Production` so `DevSeed`, `MapOpenApi`, startup migration, and the E2E scheme are all inert.
- **Deploy (user/CI):** the real `deploy-api` step publishing the image (built from `backend/src/LeafLedger.Host/Dockerfile`) to the host.

### P7-WP01c — Azure PostgreSQL + production migration path — *user (resource) + LL Backend Dev (migration step)*
- **Provision (user):** Azure Database for PostgreSQL **Flexible Server**, engine version aligned to local `postgres:17`; `require`/`verify-full` TLS; network access restricted to the API (private endpoint/VNet or firewall allowlist); an application role/password; the connection string stored as a secret.
- **Migration step (LL Backend Dev):** because the Host applies migrations **only in Development**, add a **deploy-time migration execution** — an EF Core **migration bundle** (`dotnet ef migrations bundle`) or a one-shot job/init step run against the production DB **before** the new API instances serve traffic, executed as a gated pipeline step (D-DEPLOY-MIGRATIONS). Expand-contract; rollback-safe. **No** change to the Host's Development-only startup migration.
- **Fresh-start:** the DB launches **empty** (ADR-0001 — no bulk data migration).

### P7-WP01d — Azure SignalR + production Entra + secrets — *user (portal) + LL Backend/Frontend Dev (wiring)*
- **Azure SignalR (user):** provision an Azure SignalR Service (**Default mode**); supply its connection string to the API as `ConnectionStrings__AzureSignalR` (secret). The `Program.cs` seam activates automatically — **no code change** (verify inertness when absent stays true).
- **Entra (user):** a production **SPA** app registration (redirect URIs = the Azure Static Web Apps domain(s); SPA platform; the API scope pre-authorized) and the **API** app registration (App ID URI / audience = the configured client-id GUID; expose `ledger.write` + `ledger.read`). Record the resulting IDs into the frontend `VITE_MSAL_*` (WP01a) and backend `Authentication:*` (WP01b) values.
- **Secrets (user + LL Backend Dev):** all credentials in GitHub Actions secrets and/or Key Vault / platform app settings; `.env.example` lists **names only**; `gitleaks` stays green. No secret enters the repo or the OpenAPI/TS artifacts.

### P7-WP01e — Deploy pipeline gates (staged deploy + probes) — *LL Backend Dev*
- Replace the placeholder `deploy-api` / `deploy-frontend` jobs in `.github/workflows/main.yml` with **real, gated, staged** deploys (D-DEPLOY-GATES) that keep the existing `needs: [frontend, backend, integration, property, contract, gitleaks, dependency-audit, e2e-smoke]` gate and then: run the WP01c migration step → deploy to an **API staging slot + a SWA preview environment** → probe **`/health` and `/integrity`** (Part 5 §1.2) → **promote/swap** on green, **fail-closed** (no promotion) otherwise. `pr.yml` is unchanged (PRs stay fast).

## Non-goals (explicitly deferred)

- **No production onboarding / space-creation / seed** — a first user needs a space + membership + starting accounts; that is a **separate feature WP** (see Open questions). This WP stands up infra and health-verifies it; it does **not** make the app *usable* for a brand-new tenant.
- **No data migration from the OLD stack** — fresh-start launch (ADR-0001). The production DB is empty.
- **No Azure Blob / Azure AI Document Intelligence / FX-market-data provisioning** — those side services belong to their feature phases (attachments, imports, FX) — Phase 4/5, not here.
- **No custom domain / DNS / CDN tuning / WAF** beyond what Azure Static Web Apps + the Azure host provide by default — a follow-up ops item.
- **No load/perf/DR/backup-policy hardening beyond enabling platform defaults** (PITR is inherent to Flexible Server) — Phase 6 / a later ops WP.
- **No IaC/Terraform authoring** unless the user asks — provisioning is portal/CLI by the user for M-scale; recorded as a carry-forward if repeatable IaC is wanted.
- **No change to application/domain behavior, no new endpoint, no OpenAPI/contract/migration authoring** (the WP01c step *runs* existing migrations; it does not add one), **no golden-fixture / accounting change.**
- **No enabling of the E2E auth seam or `DevSeed` in production** — both must stay Development-gated (a verification item, not a change).

## Decisions (front-loaded, non-accounting — all APPROVED 2026-07-17)

Nine decisions. **None is an accounting decision** (infrastructure/config only). **All approved by the user 2026-07-17** — each is now a binding implementation constraint.

- **D-DEPLOY-FRONTEND-HOST — the SPA host. DECIDED: Azure Static Web Apps** (the user's chosen target, 2026-07-17). Static build of `app/dist` (`app_location: app`, `output_location: dist`) with a `navigationFallback` SPA history fallback; co-located with the Azure API/DB/SignalR; first-class Entra-bound redirect URIs; built-in per-branch/PR **preview environments** (consumed by D-DEPLOY-GATES); a **linked-backend** option for same-origin `/api` (see D-DEPLOY-API-BASEURL); deployed via the `Azure/static-web-apps-deploy` GitHub Action. *Superseded alternative:* Vercel (the earlier trial target) — replaced by SWA for Azure co-location and native staging environments; the placeholder `deploy-frontend` comment still says "Vercel" and is corrected in WP01e.
- **D-DEPLOY-API-HOST — the API host. DECIDED: Azure App Service for Containers (Linux)** — continuity with the OLD stack (App Service Linux B1), first-class deployment-slot support (needed for D-DEPLOY-GATES staged swap), and it runs the existing `Dockerfile` unchanged. *Superseded alternative:* Azure Container Apps (better autoscale/scale-to-zero, revisions instead of slots) — recorded as a fallback if scale-to-zero economics later dominate.
- **D-DEPLOY-DB — the database. DECIDED: Azure Database for PostgreSQL Flexible Server, engine major version = local `postgres:17`, TLS required, private networking to the API.** Matches ADR-0001 (PostgreSQL system-of-record) and the local image. *Superseded alternative:* a serverless Postgres (e.g. Neon) — rejected for the production system-of-record (Azure co-location with the API + SignalR + PITR + VNet is preferred); a serverless PG could serve a throwaway preview only.
- **D-DEPLOY-API-BASEURL — how the SPA reaches the API (the crux for "nothing is shown"). DECIDED: Azure Static Web Apps same-origin routing to the Azure API** — either a SWA **linked backend** or a `staticwebapp.config.json` `rewrite` of `/api/*` and `/hubs/*` (negotiate) to the API origin, keeping the SPA same-origin so `client.ts` stays `baseUrl='/'` and no CORS is needed. This is the smallest change and preserves the existing bearer flow; the SignalR WebSocket itself connects **directly** to Azure SignalR (Default mode), so only the negotiate HTTP call is proxied. **Implementation preference: the linked backend if the Standard-SKU/region rules fit, else the `rewrite` route** — both keep `client.ts` at `baseUrl='/'`; the absolute `VITE_API_BASE_URL`+CORS variant is the fallback only if same-origin proves infeasible. Because the same-origin route is chosen, **WP01b does not configure CORS and WP01a does not touch `client.ts`.**
- **D-DEPLOY-SIGNALR — realtime. DECIDED: provision Azure SignalR Service (Default mode) and supply `ConnectionStrings:AzureSignalR`** so the merged P3-WP08 seam activates with no code change. *Superseded alternative:* run the self-hosted hub in production — rejected as the scale-out anti-pattern the spec replaced (Part 2 §8).
- **D-DEPLOY-MIGRATIONS — production schema application. DECIDED: a deploy-time EF migration bundle run as a gated pipeline step before new instances serve traffic** (the Host migrates only in Development). Expand-contract, rollback-safe. *Superseded alternative:* startup migration in Production — rejected (startup migrations race multi-instance rollouts and couple app boot to DDL).
- **D-DEPLOY-ENTRA — identity. DECIDED: separate production SPA + API Entra app registrations with redirect URIs bound to the Azure Static Web Apps domain(s), audience = the configured client-id GUID, scopes `ledger.write`/`ledger.read`, authority `common/v2.0` (matching `appsettings.json`).** *Superseded alternative:* reuse the existing dev registration — rejected (keep environments isolated).
- **D-DEPLOY-SECRETS — secret custody. DECIDED: GitHub Actions encrypted secrets for the pipeline + Azure Key Vault / platform app settings for runtime; `.env.example` documents names only; `gitleaks` stays blocking.** *Superseded alternative:* commit an encrypted `.env` (sops/age) — rejected for M-scale.
- **D-DEPLOY-GATES — the deploy flow. DECIDED: keep the existing `main` gates, then migrate → deploy to an API staging slot + a SWA preview environment → probe `/health` + `/integrity` → promote/swap on green, fail-closed otherwise** (Part 5 §1.2 verbatim). *Superseded alternative:* deploy straight to production on green tests — rejected (no probe safety net).

## Accounting decisions

**None required — no LL Accounting Expert consult.** This WP provisions and wires infrastructure; it computes no value, ports no rule, and changes no domain code. The standing invariants are respected, not decided: money stays integer minor units + ISO currency; the deferred balance trigger + RLS ship as part of the existing migrations the WP01c step *runs* (unchanged). The `/integrity` probe (WP01e) reads the already-merged deterministic trial-balance hash (P2-WP07) — it verifies, it does not compute new accounting.

## Golden fixtures

**None required.** No accounting function is ported and no financial value is computed. Correctness of this WP is pinned by **operational acceptance checks** (health/readiness/integrity probes returning the expected status; a real sign-in reaching the API; a live-update over Azure SignalR; the migration step applying cleanly to an empty DB; secret-scan + contract byte-stability gates staying green) — see acceptance criteria. Recorded so QA does not expect a golden artifact. This mirrors the P1-WP02 precedent (a deployment/infra WP is pinned by runtime probes + gates, not fixtures).

## Source material — salvage vs rewrite

Pinned OLD repo: `Lokkeccs/Accounting` @ `085bedba467e3d46d3889db3bc80ea023e69756e`.

- **Reference only (topology continuity, non-port):** the OLD backend ran on **Azure App Service (Linux, B1)** with Cosmos + Blob + Azure Functions (Part 1). This WP keeps the *App Service* choice (D-DEPLOY-API-HOST) but replaces Cosmos with Azure PostgreSQL (ADR-0001) and the self-hosted hub with Azure SignalR (Part 3 §2). None of the OLD deploy scripts (which had the §9 build/deploy issues) are ported.
- **Reuse (this repo):** the merged `Dockerfile`, `docker-compose*.yml`, the EF migrations, the `Program.cs` Azure-SignalR + `/health` + `/health/ready` seams, the P2-WP07 `/integrity` endpoint, and the `main.yml` gate set — all consumed as-is; this WP wires them to cloud resources, it does not rewrite them.

## Dependencies

- **No new application runtime dependency** (frontend or backend). Possible **CI tooling**: `dotnet-ef` (for the migration bundle) and the platform deploy actions (e.g. `azure/webapps-deploy`, `Azure/static-web-apps-deploy`) — recorded here per the repo dependency rule; all are build/deploy-time, not shipped.
- **New cloud resources (provisioned by the user, not the agent):** an Azure Static Web Apps resource; an Azure App Service (or Container Apps) app; an Azure Database for PostgreSQL Flexible Server; an Azure SignalR Service; production Entra SPA + API app registrations.
- **New secrets (held outside the repo):** `ConnectionStrings__Postgres`, `ConnectionStrings__AzureSignalR`, the DB admin/app password, the SWA deployment token, and any Azure deploy tokens — GitHub Actions secrets and/or Key Vault / platform app settings.
- **No migration authored, no OpenAPI/TS regeneration, no REST contract change** — `app/src/api/**` and `backend/openapi/**` stay byte-unchanged (the contract gate must produce no diff). The only conditional application-code touch is the `VITE_API_BASE_URL` seam in `client.ts` **iff** D-DEPLOY-API-BASEURL selects the absolute-URL route.

## File list (implementation target — code/config only; portal actions are user steps)

**Frontend (LL Frontend Dev — WP01a)**
- `staticwebapp.config.json` (new, app root `app/`) — `navigationFallback` SPA fallback + `/api` + `/hubs` (negotiate) routes / linked backend.
- `app/.env.example` (modified) — document any new production var **names** (e.g. `VITE_API_BASE_URL`) — names only.
- `app/src/api/client.ts` (modified **only if** D-DEPLOY-API-BASEURL = absolute-URL) — a `VITE_API_BASE_URL`-aware `baseUrl` seam; likewise the SignalR hub-URL builder.

**Backend (LL Backend Dev — WP01b/c/e)**
- `backend/src/LeafLedger.Host/appsettings.Production.json` (new) — non-secret production defaults (`AllowedHosts`, logging, CORS origins if applicable); **no secrets** (secrets arrive via env/Key Vault).
- `.github/workflows/main.yml` (modified) — real, gated, staged `deploy-api` / `deploy-frontend` + the migration-bundle step + the `/health`+`/integrity` probe + promote/swap; `needs` chain preserved.
- (CI) a migration-bundle build/run step (workflow + optional `dotnet ef migrations bundle` invocation).

**Docs (LL Docs Editor / this plan)**
- `docs/rebuild/plans/P7-WP01-production-deployment-foundation.md` (this file).
- `docs/rebuild/status.md` (Phase 7 rows + session log).
- A short **runbook** (`docs/architecture/rebuild/deployment.md`, optional) — resources, env-var/secret catalog (names only), deploy/rollback steps, probe URLs.

**No** `backend/openapi/**`, `app/src/api/schema.d.ts`, or EF-migration authoring.

## Acceptance criteria (concrete, verifiable — grouped by sub-WP)

### P7-WP01c (DB + migrations)
1. The Azure PostgreSQL Flexible Server exists, engine major version matches local `postgres:17`, TLS is required, and network access is restricted to the API host (no public-open `0.0.0.0/0`).
2. The deploy-time migration step applies **all** EF migrations to the empty production DB cleanly (idempotent re-run is a no-op) and is proven against a scratch instance; `HasPendingModelChanges()` is false afterward.

### P7-WP01b (API)
3. The Host image (from the existing `Dockerfile`) runs on the chosen Azure host under `ASPNETCORE_ENVIRONMENT=Production`; `GET /health` returns 200 and `GET /health/ready` returns 200 once the DB is reachable (503 while not).
4. Development-only behavior is **inert** in production: no `DevSeed`, no `MapOpenApi` (`GET /openapi/v1.json` is 404), no startup migration, and the E2E auth scheme is **not** registered (a check confirms `Authentication:E2E:Enabled` has no effect outside Development).
5. `AllowedHosts` is scoped to the API domain; **CORS is absent** — the decided same-origin linked-backend/rewrite route (D-DEPLOY-API-BASEURL) means the API is reached same-origin and no cross-origin CORS policy is configured.

### P7-WP01d (SignalR + Entra + secrets)
6. Azure SignalR is provisioned; with `ConnectionStrings:AzureSignalR` present the API activates the seam (a startup log / negotiate response confirms Azure SignalR), and with it absent the self-hosted hub still serves (seam inertness preserved) — **no code change** was needed.
7. A real Entra sign-in from the deployed SPA obtains a token the API accepts (JWKS + audience + tenant allowlist pass); redirect URIs resolve to the Azure Static Web Apps domain; scopes `ledger.write`/`ledger.read` are granted.
8. **No secret is in the repo or artifacts:** `gitleaks` is green, `.env.example` lists names only, and the contract gate shows `app/src/api/**` + `backend/openapi/**` byte-unchanged.

### P7-WP01a (Static Web Apps)
9. The production Azure Static Web Apps build serves the SPA; deep links (e.g. `/reports/trial-balance`) resolve via the `navigationFallback` (no 404); `VITE_E2E_AUTH` is unset in the production build.
10. The SPA reaches the API: `/api/*` calls succeed against the Azure origin (via linked backend / rewrite or configured base URL) and `/hubs/*` negotiate + the direct Azure SignalR WebSocket establish (live-update works) — an authenticated smoke against production observes a report GET succeeding.

### P7-WP01e (gates) + overall
11. `main` runs the existing gates, then **migrate → staged deploy → `/health`+`/integrity` probe → promote/swap**, and **fails closed** (no promotion) if either probe is non-200; `pr.yml` is unchanged.
12. **No out-of-scope drift:** no domain/endpoint/OpenAPI/migration authoring; the only application-code touch is the conditional `VITE_API_BASE_URL` seam; a scope scan confirms the diff is limited to the file list; all pre-existing frontend/backend gates stay green.

## Split seam

**Required.** Implement as five ≤2-day sub-WPs (**P7-WP01c → b → d → a → e**), each independently verifiable (a portal/probe check or a gate run) as listed above. Cloud-provisioning steps are **user actions** interleaved with the Dev agents' config edits; an agent must never attempt to create cloud resources or enter secrets.

## Open questions / carry-forwards

- **Production onboarding (BLOCKING for a *usable* launch, not for infra stand-up).** With `DevSeed` Development-only and no space-creation/membership/starting-chart endpoints yet (CoA write = P4-WP05; admin/invitations = P4-WP18), a first Entra user hits `403 auth.not_a_member` against an empty DB. **A minimal production onboarding path (create space + Owner membership + a starting chart) is a required predecessor to a public launch** and should be scoped as its own WP (route to LL Architect once P4-WP05/WP18 land, or a dedicated minimal-onboarding WP). Infra (this WP) can be stood up and health-verified before it exists.
- **`VITE_DEMO_SPACE_ID` / `VITE_DEMO_BASE_CURRENCY` are dev seams.** Production must replace the demo-space stand-in with a real space picker / `GET /spaces` (a later frontend WP) — until then a production build has no real space to point at (ties to the onboarding gap).
- **Custom domain / DNS / WAF / CDN** — a follow-up ops item once the SWA + Azure origins are live.
- **IaC (Terraform/Bicep)** — if repeatable, reviewable provisioning is wanted, author it in a follow-up; portal/CLI provisioning is the M-scale default here.
- **Backup/DR/observability hardening** (App Insights, alerting, PITR retention tuning, load tests) — Phase 6 / a later ops WP; enable platform defaults now.
- **Azure SignalR correlation-id / staleness UX** — remains the P3-WP08 carry-forward, not gated here.

## Definition of done

All 12 acceptance criteria pass across the five sub-WPs; each sub-WP moves `planned → in-progress → verify → done` under LL QA Reviewer acceptance; secrets remain outside the repo; the contract + gitleaks + all pre-existing gates stay green; no domain/OpenAPI/migration authoring; the deploy pipeline is real, gated, and fails closed. **Infra is "done" when production is health/integrity-verified; the app is "launch-usable" only after the onboarding predecessor above is resolved** — recorded so "done infra" is not mistaken for "ready to onboard real tenants."
