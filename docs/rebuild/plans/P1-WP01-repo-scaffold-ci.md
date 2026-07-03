# P1-WP01 — Repo scaffold + CI skeleton

State: planned (awaiting user approval)          Last session: 2026-07-03

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
*(filled by LL Backend Dev / LL Frontend Dev during implementation)*

## QA verdict
*(filled by LL QA Reviewer only)*
