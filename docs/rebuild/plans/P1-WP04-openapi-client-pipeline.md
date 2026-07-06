# P1-WP04 — OpenAPI → generated TypeScript client pipeline

- **Phase:** 1 (foundation)
- **State:** done — merged to main via PR #8 (`24c21df`) 2026-07-06; QA PASS 2026-07-06
- **Owner (implementation):** LL Backend Dev (contract emission) → LL Frontend Dev (generation + client + CI gate)
- **Depends on:** P1-WP01 (repo scaffold + CI + ESLint boundaries), P1-WP02 (Host + compose), P1-WP03 (SharedKernel) — all done/merged
- **Estimated size:** ≤ 2 days
- **Spec sources:** `docs/architecture/rebuild/03-target-architecture.md` §6 (API design — "OpenAPI is the single contract"), §7 (frontend structure — `api/` = GENERATED client + types); `docs/architecture/rebuild/04-implementation-plan.md` §1 (Phase-1 exit: "OpenAPI → TS client generation pipeline"); `docs/architecture/rebuild/05-quality-and-maintainability.md` §22 (PR pipeline includes "contract diff (OpenAPI)"), §40 (SemVer `/api/v1`, OpenAPI diff gate); `docs/architecture/rebuild/06-feature-roadmap.md` (line 26); `docs/architecture/rebuild/07-vibe-coding-playbook.md` §105 (data access ONLY through generated client, never edit — regenerate)

## Goal
Stand up the **single-contract pipeline**: the backend emits a canonical OpenAPI document; the frontend deterministically generates its typed client + models from that document into `app/src/api/`; and CI enforces a **contract-diff gate** that fails on any drift between committed contract, committed generated client, and a fresh regeneration. This is the mechanism that (per Part 2 §6 / Part 5) "kills payload drift permanently." The pipeline must work end-to-end today with a minimal anchor endpoint; real business endpoints are added by their own WPs later and inherit this machinery for free.

## Scope (what this WP delivers)
1. **Backend OpenAPI document emission** — wire the first-party .NET 9 OpenAPI stack (`Microsoft.AspNetCore.OpenApi`: `AddOpenApi()` + `MapOpenApi()`), and a **build-time document generator** (`Microsoft.Extensions.ApiDescription.Server`) that writes a deterministic `backend/openapi/leafledger-v1.json` on `dotnet build` (no running server, no Postgres required). The JSON artifact is **committed** as the canonical contract.
2. **Anchor endpoint** — one minimal, non-accounting endpoint `GET /api/v1/meta` returning a typed `MetaResponse { name: string; version: string }`. Its only purpose is to make the contract non-empty and prove the round-trip (server schema → generated TS type → application-layer consumption). Framed as pipeline scaffolding, not a feature.
3. **Frontend generation script** — `npm run gen:api` reads the committed `backend/openapi/leafledger-v1.json` and generates typed output into `app/src/api/` (`schema.d.ts` = pure generated types; `client.ts` = thin, stable, committed bootstrap that instantiates the typed `openapi-fetch` client). Generation is deterministic (pinned tool versions; normalized formatting) so the diff gate is not flaky. Committed generated output is checked in.
4. **Application-layer consumer** — a small typed wrapper (`app/src/application/meta.ts`, e.g. `getMeta()`) that calls the generated client and returns contract-typed data. Proves the boundary (`features → application → api`), that generated types are consumable, and that no direct `fetch` is used. Covered by a vitest with a mocked client/fetch.
5. **CI contract-diff gate** — a new `contract` job in `pr.yml` and `main.yml` that: builds the backend (re-emits the OpenAPI JSON) → runs `npm run gen:api` → `git diff --exit-code` on `backend/openapi/**` and `app/src/api/**`. Any stale contract or stale client fails the build. This is the "contract diff (OpenAPI)" gate named in Part 5 §22. The gate must be **proven red** (a schema change without regeneration fails), mirroring P1-WP01's red-gate proof.
6. **README refresh** — update `app/src/api/README.md` to state the final tool, the regen command (`npm run gen:api`), and that `client.ts` is the only stable hand-authored file in the folder (everything else is generated).

## Non-goals (explicitly deferred)
- **No TanStack Query provider / app-shell / QueryClient wiring** — the query layer, providers, and error boundaries are the **P3 re-platform**. This WP delivers the typed client + one plain application-layer wrapper; hand-written TanStack Query hooks come later. Do **not** install `@tanstack/react-query` here.
- **No real business endpoints** — no journal entries, accounts, reports, imports, etc. Only the `meta` anchor. Each real endpoint arrives in its own WP and reuses this pipeline.
- **No API-versioning library** (`Asp.Versioning.*`) — `/api/v1` is a literal route prefix for now. Formal SemVer versioning + deprecation windows (Part 5 §40) are a later WP.
- **No ProblemDetails / structured error-contract standardization** — the `422`/ProblemDetails conventions (Part 2 §6) are wired when the first write endpoint lands (Phase 2). Note only.
- **No `app/shared` / `app/companion` restructure** — the two-client folder layout (Part 3 §7) is the P3 re-platform. Today the client lives in `app/src/api`.
- **No MSAL / auth glue, no SignalR** — auth headers on the client and cache-invalidation pings are later WPs. `client.ts` sets base URL only.
- **No orval / TanStack-hook codegen** — generating hooks into `src/api` would put the query layer in the wrong boundary. Generate **types + a typed fetch client only**; hooks stay hand-written in the application layer.

## Source material (salvage vs rewrite)
Per `04-implementation-plan.md` §5 this is a **greenfield build, not a port**. The OLD system (`Lokkeccs/Accounting`) tolerated client/server contract drift forever (Part 2 §6: `spaceId OR SpaceId`, `seq OR Seq`, `payload OR payloadJson`) — the exact anti-pattern this pipeline exists to make impossible. There is **no old behavior to preserve**; nothing to salvage. The OLD repo not being reachable during planning does not block this WP (nothing is ported).

## Golden fixtures
**None required.** This is pure build/tooling infrastructure with no accounting behavior. Determinism of the generated artifacts (contract JSON + `schema.d.ts`) is pinned by the CI diff gate itself (AC-7), not by golden fixtures. No LL Accounting Expert consultation needed (no Swiss accounting/VAT/FX decision in scope).

## Tooling decision
- **Backend contract emission:** first-party `Microsoft.AspNetCore.OpenApi` (ships with .NET 9; Swashbuckle is de-emphasized) + `Microsoft.Extensions.ApiDescription.Server` for **build-time** document generation. Rationale: build-time emission needs no running server or Postgres in CI, and produces a committable artifact the diff gate can hash. Fallback if build-time generation proves flaky: boot the Host with no connection string and `curl /openapi/v1.json`.
- **Frontend generation:** `openapi-typescript` (generates `schema.d.ts` types) + `openapi-fetch` (tiny, fully-typed runtime fetch wrapper). Rationale: it yields exactly "types + a typed client" without codegen'ing framework hooks, so the `features → application → api` boundary is preserved and the application layer owns the (future) TanStack Query hooks. `openapi-fetch` also honors the "no direct `fetch`" rule via a single typed client.
- **This tooling choice is a candidate ADR** (contract-pipeline tooling). The ADR **log/index is formalized in P1-WP05**; record "propose ADR-0003 — API contract pipeline" as a note for that WP rather than creating an ADR file here (avoids colliding with P1-WP05's index work).

## Dependencies to add (record per repo rule "no new dependencies without a plan-file entry")
- **Backend** (`LeafLedger.Host.csproj`):
  - `Microsoft.AspNetCore.OpenApi` (9.0.x) — OpenAPI document services.
  - `Microsoft.Extensions.ApiDescription.Server` (9.0.x, `PrivateAssets=all`) — build-time doc emission; build-only, not shipped.
- **Frontend** (`app/package.json`):
  - `openapi-typescript` (devDependency) — types generator.
  - `openapi-fetch` (dependency) — typed runtime client.
- Pin exact versions; `npm audit --audit-level=high` and `dotnet list package --vulnerable` must stay clean (AC-10).

## File list (implementation targets)
**Backend (LL Backend Dev):**
- `backend/src/LeafLedger.Host/LeafLedger.Host.csproj` — add the two packages; set `<OpenApiDocumentsDirectory>` + `<OpenApiGenerateDocumentsOnBuild>true</OpenApiGenerateDocumentsOnBuild>` (or equivalent) to emit `leafledger-v1.json`.
- `backend/src/LeafLedger.Host/Program.cs` — `AddOpenApi("v1")`, `MapOpenApi()` (dev), map `GET /api/v1/meta` → typed `MetaResponse`.
- `backend/openapi/leafledger-v1.json` — **new**, committed canonical contract (generated).

**Frontend (LL Frontend Dev):**
- `app/package.json` — add deps + scripts: `gen:api` (generate from `../backend/openapi/leafledger-v1.json`), optionally `check:contract`.
- `app/src/api/schema.d.ts` — **new**, generated types (committed).
- `app/src/api/client.ts` — **new**, stable committed `createClient<paths>()` bootstrap (base URL only).
- `app/src/api/README.md` — update (tool, regen command, `client.ts` note).
- `app/src/application/meta.ts` — **new**, `getMeta()` typed wrapper over the generated client.
- `app/src/application/meta.test.ts` — **new**, vitest with mocked client/fetch.

**CI:**
- `.github/workflows/pr.yml` — add `contract` job (build backend → `gen:api` → `git diff --exit-code`).
- `.github/workflows/main.yml` — same `contract` job.

## Acceptance criteria (as concrete tests — all must pass)
**Contract emission (backend)**
1. `dotnet build --configuration Release` emits `backend/openapi/leafledger-v1.json`; the committed file matches a fresh build: `git diff --exit-code -- backend/openapi` is clean. No running server / Postgres required to generate it.
2. The document contains `GET /api/v1/meta` with a response schema `MetaResponse { name: string; version: string }` (both required, typed `string`). OpenAPI `info.version` reflects `v1`.
3. Backend Release build is **0 warnings / 0 errors** (`TreatWarningsAsErrors` on); architecture tests still **3/3** (SharedKernel isolation unaffected).

**Generation + typed client (frontend)**
4. `npm run gen:api` regenerates `app/src/api/schema.d.ts` from the committed contract; the committed output matches a fresh generation: `git diff --exit-code -- app/src/api` is clean.
5. `app/src/application/meta.ts` `getMeta()` returns contract-typed data (its return type is inferred from `schema.d.ts`); a deliberate misuse of a `MetaResponse` field is a **compile error** and `npm run typecheck` (`tsc -b`) is green with correct usage. Generated `schema.d.ts` typechecks as part of the app build.
6. Unit test: `getMeta` returns the parsed typed object given a mocked client/fetch (no network); `npm test` (vitest) green.

**Boundaries & gates**
7. **Contract-diff CI gate** exists in `pr.yml` and `main.yml`: it builds the backend, runs `npm run gen:api`, and fails on any diff in `backend/openapi/**` or `app/src/api/**`. **Red-gate proof:** changing the `meta` response schema (or endpoint) without regenerating fails the job — reproduce and record it (as P1-WP01 did for its gates).
8. Boundary enforcement holds: `getMeta` lives in the application layer and imports only `src/api`; a `src/features/**` import of `src/api` still fails lint (existing rule); no direct `fetch` — the client uses `openapi-fetch` (existing `no-restricted-globals`/`no-restricted-properties` rules pass). `src/api/**` remains lint-ignored.
9. All frontend gates green: `npm run lint`, `npm run typecheck`, `npm test`, `npm run check:page-budget`.
10. Dependency-audit gate stays green with the new packages: `npm audit --audit-level=high` clean and `dotnet list package --vulnerable --include-transitive` reports none.

## Manual / human steps
- None requiring LL Accounting Expert (no accounting decision in scope).
- Implementation order (single WP, two agents): **LL Backend Dev first** (packages + `/api/v1/meta` + emit `leafledger-v1.json`), then **LL Frontend Dev** (deps, `gen:api`, `client.ts`, `getMeta` + test, CI `contract` job + red-gate proof, README). Both hand to LL QA Reviewer at `verify`.

## Risks & notes
- **Build-time doc generation boots the app** (via the `GetDocument` insider tool) to enumerate endpoints. Ensure startup succeeds with **no** `ConnectionStrings:Postgres` set (readiness health check is conditionally registered — Program.cs already guards on empty connection string), so no Postgres is needed in CI. Fallback documented above.
- **Determinism is the whole game.** The diff gate is flaky if either artifact's ordering/formatting is non-deterministic across machines. Mitigations: pin exact tool/package versions; if the OpenAPI JSON or `schema.d.ts` formatting varies, add a normalization step (e.g., stable key ordering / a formatter run) as part of emission/`gen:api` so CI and local produce byte-identical output. AC-1/AC-4 are the guards.
- **Node/tool availability in the `contract` job:** the job needs both .NET (build) and Node (`gen:api`) — set up both toolchains (the existing `dependency-audit` job already shows the dual-setup pattern).
- **Scope discipline:** if wiring the pipeline surfaces pressure to add auth headers, ProblemDetails, versioning, or TanStack Query, **stop and raise a WP** — those are explicit Non-goals here.
- **`client.ts` is intentional stable glue,** not stale generated output — call this out for QA so the "never hand-edit generated files" rule isn't misread as forbidding it.

## Implementation notes
- **2026-07-06 (LL Backend Dev) — backend half (Scope items 1–2, ACs 1–3):**
  - `LeafLedger.Host.csproj`: added `Microsoft.AspNetCore.OpenApi` 9.0.0 and `Microsoft.Extensions.ApiDescription.Server` 9.0.0 (`PrivateAssets=all`, build-only). Set `OpenApiGenerateDocumentsOnBuild=true` + `OpenApiDocumentsDirectory=…/backend/openapi`.
  - `Program.cs`: `AddOpenApi("v1", …)` with a document transformer setting `info.title="LeafLedger API"`, `info.version="v1"`; `MapOpenApi()` in Development only; `GET /api/v1/meta` → `internal sealed record MetaResponse(string Name, string Version)` (both required strings), tagged `Meta`.
  - **Contract emitted at build time, no server/Postgres** → committed [backend/openapi/leafledger-v1.json](../../../backend/openapi/leafledger-v1.json). Confirmed AC-2: `GET /api/v1/meta` present with `MetaResponse{name,version}` both `required`+`string`; `info.version=v1`.
  - **Deviation (in-scope, note for QA):** the generator tool (`Microsoft.Extensions.ApiDescription.Server`) has no MSBuild knob for the output filename — it writes `<ProjectName>.json` (`LeafLedger.Host.json`). Added a tiny `AfterTargets="GenerateOpenApiDocuments"` MSBuild `Move` target that renames it to the canonical `leafledger-v1.json` (the path the plan + frontend gen depend on). No stray file remains.
  - **Determinism (AC-1 guard):** two `--no-incremental` regenerations produced **byte-identical** output (SHA-256 `63E5…FA5C` both runs).
  - **Gates:** solution `dotnet build -c Release` = **0 warnings / 0 errors**; architecture tests **3/3** (SharedKernel isolation unaffected; the new packages are Host-only).
  - No auth/ProblemDetails/versioning-lib/TanStack introduced (Non-goals respected).
  - **Next (LL Frontend Dev):** `openapi-typescript`+`openapi-fetch` gen into `app/src/api/`, `getMeta()` wrapper + vitest, and the CI `contract` diff gate with red-gate proof (ACs 4–10).
- **2026-07-06 (LL Frontend Dev) — frontend half (Scope items 3–6, ACs 4–10):**
  - Deps (pinned): `openapi-typescript@^7.13.0` (dev), `openapi-fetch@^0.13.8` (runtime). Script `gen:api` = `openapi-typescript ../backend/openapi/leafledger-v1.json -o ./src/api/schema.d.ts`.
  - **Peer-conflict fix:** `openapi-typescript@7` declares peer `typescript@^5.x`; repo runs `typescript@6.0.3`. Added [app/.npmrc](../../../app/.npmrc) `legacy-peer-deps=true` so `npm install` and CI `npm ci` resolve identically (the tool uses the backward-compatible TS compiler API). **Deviation from plan** (new build-config file), in-service of the approved dep.
  - Generated [app/src/api/schema.d.ts](../../../app/src/api/schema.d.ts) (`paths`/`operations.GetMeta`/`components.schemas.MetaResponse{name,version}`). Added stable bootstrap [app/src/api/client.ts](../../../app/src/api/client.ts) (`createClient<paths>`, same-origin base; auth/base wiring deferred to P3). Updated [app/src/api/README.md](../../../app/src/api/README.md).
  - Application layer: [app/src/application/meta.ts](../../../app/src/application/meta.ts) `getMeta(client = apiClient)` returns contract-typed `Meta`; [meta.test.ts](../../../app/src/application/meta.test.ts) covers map + throw-on-no-data (mocked client, no network).
  - **Determinism:** added targeted [.gitattributes](../../../.gitattributes) forcing LF on `backend/openapi/*.json` + `app/src/api/schema.d.ts` (both already emit LF; pins cross-OS so the byte-diff gate can’t flake on `core.autocrlf`).
  - **CI contract gate** added to [pr.yml](../../../.github/workflows/pr.yml) + [main.yml](../../../.github/workflows/main.yml): setup dotnet+node → build Host (re-emit contract) → `npm ci` → `npm run gen:api` → `git add -N` + `git diff --exit-code -- backend/openapi app/src/api`. Wired into `main.yml` deploy `needs`. (`git add -N` also catches newly-generated untracked files.)
  - **Red-gate proven locally:** mutating `MetaResponse` (+field) then rebuild+regen → drift diff exit `1` for both contract and schema (gate FAILS); after restore, regen matches committed baseline exit `0` for both (gate PASSES + deterministic). Encoding note: the mutation must be written UTF-8 (PS5.1 `Set-Content` default corrupted the source on the first attempt).
  - **Gates:** `npm run lint`/`typecheck`/`test` (3 passed)/`check:page-budget` all green; `npm audit --audit-level=high` = 0 vulns; `dotnet list package --vulnerable` = none; solution Release 0/0; arch 3/3.
  - No TanStack Query/provider, no auth, no real endpoints, no versioning lib introduced (Non-goals respected).
  - **Handoff:** ready for LL QA Reviewer. Working tree carries the new files uncommitted (LL Git commits later).

## QA verdict
**PASS — 2026-07-06 (LL QA Reviewer).** All 10 acceptance criteria met; independently reproduced.

Independent verification (my run):
- **AC-1** — `dotnet build LeafLedger.sln -c Release --no-incremental` emits `backend/openapi/leafledger-v1.json`; regeneration byte-identical (schema hash stable; contract deterministic). Build needs **no** Postgres (Program.cs guards on empty connection string; build-time `GetDocument` boots headless). PASS.
- **AC-2** — contract: `info.title="LeafLedger API"`, `info.version="v1"`, path `/api/v1/meta` → `operationId=GetMeta`, `MetaResponse.required=[name,version]`, both `type=string`. PASS.
- **AC-3** — solution Release **0 warnings / 0 errors**; ArchitectureTests **3/3** (SharedKernel isolation unaffected; new packages Host-only). PASS.
- **AC-4** — `npm run gen:api` regenerates `schema.d.ts` byte-identical (`schema_fresh=True`). PASS.
- **AC-5** — `getMeta` returns contract-typed `Meta` from `data`; `tsc -b` green. PASS.
- **AC-6** — vitest **3 passed** (incl. `getMeta` map + throw-on-no-data, mocked client). PASS.
- **AC-7** — `contract` diff gate present in `pr.yml` + `main.yml` (`build → gen:api → git add -N → git diff --exit-code`), wired into main deploy `needs`. **Red-gate independently reproduced:** source mutation changed both artifacts (`RED_*_changed=True`); restore regenerated byte-identical (`GREEN_*_restored=True`). PASS.
- **AC-8** — `getMeta` sits in the application layer importing only `src/api`; uses `openapi-fetch` (no raw `fetch`); the `features → api` ESLint restriction is intact (unchanged, present); `src/api/**` still lint-ignored. `npm run lint` green. PASS.
- **AC-9** — lint / typecheck / test / check:page-budget all exit 0. PASS.
- **AC-10** — `npm audit --audit-level=high` = 0 vulnerabilities; `dotnet list package --vulnerable --include-transitive` = none. PASS.

**Financial integrity:** N/A — no money/ledger/journal in this tooling WP; SharedKernel untouched; golden fixtures correctly N/A. **Security:** the OpenAPI document endpoint is **Development-only** (`MapOpenApi()` gated on `IsDevelopment`) so the API surface isn't served in prod; no secrets added (`.npmrc` carries only `legacy-peer-deps`); the anchor endpoint takes no input. **Hallucination scan:** every dependency/behavior traces to the plan's Dependencies + Scope; nothing invented. **Patch-layering scan:** no try/catch or self-heal; `getMeta`'s single guard names the exact expected condition (no `data`).

Notes (non-blocking):
- **N1 (traceability):** two files sit outside the plan's declared file list — [.gitattributes](../../../.gitattributes) (LF pin for the diff gate) and [app/.npmrc](../../../app/.npmrc) (`legacy-peer-deps` for the tool's TS peer vs repo `typescript@6.0.3`). Both are documented deviations with sound justification (determinism / approved-dep install). Accepted.
- **N2 (branch hygiene — for LL Git):** the working tree is on branch `P1-WP03-sharedkernel-value-types`, not a dedicated `P1-WP04-*` branch off main. Not a code defect; LL Git must branch P1-WP04 correctly (16 changed paths are exactly the P1-WP04 set).
- **N3 (security, future):** `GET /api/v1/meta` is unauthenticated — acceptable for a static, non-tenant metadata anchor (auth is an explicit Non-goal); real endpoints in later WPs must carry authorization per backend rules.

**Verdict: PASS. State → done.** Cleared for LL Git (create a P1-WP04 branch; commit the 16-path set incl. `backend/openapi/leafledger-v1.json` + `app/src/api/**`).
