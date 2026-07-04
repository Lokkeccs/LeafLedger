# P1-WP01 — Repo scaffold + CI skeleton

State: verify (implementation complete; CI-run + deliberate-red PR evidence pending push via LL Git)          Last session: 2026-07-04

## Scope
1. Monorepo folder layout: `app/`, `backend/`, `docs/` (VitePress moved in later), `help/` (placeholder), `tools/`, `tests/fixtures/golden/`.
2. Frontend scaffold in `app/`: Vite (latest **stable**) + React 19 + TS strict; ESLint flat config with the boundary rules (`features → application → api`; no direct fetch outside `api/`); vitest configured; page-budget script (ported from old `scripts/check-page-budget.cjs`, baseline empty).
3. Backend scaffold in `backend/`: `LeafLedger.SharedKernel` (empty), `LeafLedger.Host` (minimal API, `/health`), solution file, `Directory.Build.props` (net9, nullable, analyzers as errors), xunit test project, NetArchTest boundary test (trivially green on the skeleton).
4. GitHub Actions:
   - `pr.yml`: frontend lint + typecheck + vitest + page budget; backend build + test + arch test; gitleaks; dependency audit (`npm audit --audit-level=high`, `dotnet list package --vulnerable`). **All blocking.**
   - `main.yml`: pr.yml jobs + placeholder deploy jobs (no-op until P2/P3 wire real deploys).
5. Root hygiene: `.gitignore` (node, dotnet, publish output, .env), `.editorconfig`, README with pointers to specs/tracker.
6. Verify gates actually block: open a throwaway PR with a deliberate lint error and a boundary violation; confirm red; close it.

## Non-goals
- No domain code, no database, no docker-compose (P1-WP02), no OpenAPI pipeline (P1-WP04), no Vercel/Azure config, no help/docs content moves.

## Source material
- Old repo: `Accounting/eslint.config.js` (boundary rules to adapt), `scripts/check-page-budget.cjs`, `tsconfig.app.json` (strict flags).
- Specs: rebuild Part 3 §5/§7/§10, Part 5 §1.2.

## File list (expected)
`app/**` (scaffold), `backend/**` (scaffold), `.github/workflows/pr.yml`, `.github/workflows/main.yml`, `.gitignore`, `.editorconfig`, `README.md`, `tools/check-page-budget.cjs`.

## Acceptance criteria
- [ ] `npm run lint && npm run typecheck && npm test` green in `app/` on a fresh clone.
- [ ] `dotnet build && dotnet test` green in `backend/` on a fresh clone.
- [ ] PR workflow runs all gates and passes on the scaffold.
- [ ] Deliberate lint error + boundary violation in a test PR → workflow red (evidence: link/screenshot in notes).
- [ ] No file exceeds page budget; no committed artifacts; gitleaks clean.

## Manual steps for the user (not agent work)
- Branch protection on `main`: require PR + status checks (after first workflow run registers the checks).
- GitHub Environments `staging`/`production` (secrets added later, P3+).
- Renovate/Dependabot enable.

## Implementation notes
- 2026-07-04 (LL Backend Dev) — Backend scaffold implemented (scope item 3 only):
  - `backend/LeafLedger.sln`, `backend/Directory.Build.props` (net9.0, nullable, implicit usings, `TreatWarningsAsErrors`, .NET analyzers `latest-recommended`, `EnforceCodeStyleInBuild`).
  - `backend/src/LeafLedger.SharedKernel/` — intentionally empty classlib (types arrive in P1-WP03).
  - `backend/src/LeafLedger.Host/` — minimal API, `MapHealthChecks("/health")`; smoke-verified: GET /health → 200 "Healthy".
  - `backend/tests/LeafLedger.ArchitectureTests/` — xunit 2.9.2 + NetArchTest.Rules 1.3.2; 3 boundary tests encoding Part 3 §5 rules (SharedKernel references no other LeafLedger assembly; `*.Domain` free of Host/EF/AspNetCore deps; EF Core confined to `*.Infrastructure`). Trivially green on the skeleton by design.
  - Actual results: `dotnet build` succeeded; `dotnet test` 3/3 passed.
  - Remaining for this WP (other sessions): `app/` frontend scaffold, `tools/check-page-budget.cjs`, `.github/workflows/pr.yml` + `main.yml`, root `.gitignore`/`.editorconfig`/README pointers, placeholder folders (`help/`, `tests/fixtures/golden/`), deliberate-red gate verification.
  - Note: repo root has no `.gitignore` yet — `backend/**/bin|obj` must not be committed until scope item 5 lands.
- 2026-07-04 (LL Frontend Dev) — Remaining scope implemented (items 1, 2, 4, 5):
  - `app/`: Vite 8.1 + React 19.2 + TS 6.0 strict (`strict`, `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`, `noImplicitReturns` added to template flags). Local path `C:\Programming\LeafLedger\Accounting` absent — source material read from GitHub `Lokkeccs/Accounting` instead.
  - Layer folders `src/api` (generated-only, README sentinel), `src/application`, `src/features`, `src/app` (placeholder `App.tsx`).
  - `eslint.config.js` (flat): boundary rules adapted from old repo — features ⇸ `**/api/**` + `**/app/**`; application ⇸ features/app; `fetch` banned outside `src/api` (`no-restricted-globals`/`-properties`); `src/api/**` lint-ignored.
  - Vitest configured (`tests/smoke.test.ts`); `tools/check-page-budget.cjs` ported behavior-identical from old `scripts/check-page-budget.cjs` (450/550 lines, 30/40 imports, 20/28 states, baseline+strict); baseline empty; targets `src/app/App.tsx` + `src/features/**Page.tsx`.
  - Workflows: `pr.yml` (frontend gates, backend build+test, gitleaks, npm audit high+, dotnet vulnerable-package check — all blocking), `main.yml` (same + no-op deploy placeholders).
  - Root: `.gitignore` (node/dotnet/publish/.env), `.editorconfig`, README rewritten with layout + spec/tracker pointers; `help/.gitkeep`, `tests/fixtures/golden/.gitkeep`.
  - Actual results: `npm run lint`, `npm run typecheck`, `npm test` (1/1), `npm run check:page-budget` all green. Deliberate violation file (`features` importing `../api/client` + bare `fetch`) → 2 lint errors, exit 1; file removed. Backend `dotnet build`/`test` unchanged.
  - Outstanding (not agent-completable locally): first CI run on GitHub + deliberate-red **PR** evidence (acceptance criteria 3–4) — needs LL Git to push branch/PR; then branch protection etc. (manual user steps).
  - Deviation noted for LL Architect: scaffold is a single client at `app/` per this plan's wording; target spec §7 (2026-07-04) shows `app/web|companion|shared` — split deferred to P3 re-platform planning.

## QA verdict
*(filled by LL QA Reviewer only)*
