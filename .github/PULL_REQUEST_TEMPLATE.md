<!-- PR title must be: "P<phase>-WP<nn>: <title>"  e.g.  "P1-WP01: Repo scaffold and CI" -->

## Work package
- Plan file: <!-- link e.g. docs/rebuild/plans/P1-WP01-repo-scaffold-ci.md -->
- WP state after merge: <!-- verify | done -->
- QA verdict: <!-- link/summary — PRs merge only from a done WP with a PASS verdict -->

## Summary
<!-- What changed and why. Diff-oriented. -->

## Test results (paste actual output, not "should pass")
- [ ] Frontend: `lint` / `typecheck` / `test` / `check:page-budget`
- [ ] Backend: `dotnet build` / `dotnet test` (unit + boundary/NetArchTest)
- [ ] Financial-invariant tests relevant to this WP

## Review checklist
- [ ] **Invariants:** money is integer minor units; debits = credits enforced server + DB; posted entries immutable (reversal only); writes idempotent + authorized
- [ ] **Boundaries:** module/layer rules hold (NetArchTest / ESLint green); no `fetch` outside `src/api`; no direct data/infra imports
- [ ] **Budgets:** no page over 450 lines / 30 imports; no perf budget regressions
- [ ] **Docs:** WP plan notes + `docs/rebuild/status.md` updated (dated, ISO 8601); ADR added for any architectural decision
- [ ] **i18n:** new strings added to ALL locale files (frontend)
- [ ] **Traceability:** no invented accounting behavior — sourced from plan/spec/old code; golden-fixture divergences resolved via ADR
- [ ] **Security:** no secrets committed (gitleaks clean); dependency audit clean; no new deps without a plan-file entry

## Known issues / follow-ups
<!-- Proposed WPs, scope deferrals, anything the reviewer should know. -->
