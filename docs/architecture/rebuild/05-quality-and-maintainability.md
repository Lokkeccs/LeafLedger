# LeafLedger Rebuild Analysis — Part 5: Quality & Long-Term Maintainability Strategy

> Companion to [04-implementation-plan.md](./04-implementation-plan.md). Updated 2026-07-03 for the online-first + PostgreSQL target.

## 1. Quality strategy

### 1.1 Testing pyramid (targets)

| Level | Target | Gate |
|---|---|---|
| Financial invariant / property tests | Trial balance ≡ 0 for any generated posting sequence; unbalanced/closed-period/invalid-ref posts rejected; idempotent retries exactly-once; FX/VAT rounding bounds; DB balance-trigger + RLS verified directly | PR-blocking, the flagship suite |
| Domain unit (pure) | ≥ 90% coverage on `*.Domain` (C#) and the TS pre-validation mirror | PR-blocking, ratcheted |
| Golden masters | 3 reference ledgers → exact report outputs, executed against BOTH old TS and new C# domain (the porting oracle) | PR-blocking |
| Integration | API + Testcontainers PostgreSQL; EF migration round-trip; RLS cross-tenant denial tests | main-blocking |
| E2E (Playwright) | ~15 journeys incl. two-browser live-update via SignalR ping | main-blocking (smoke) + nightly (full) |
| Load/perf | Weekly scheduled; budgets: posting < 300 ms p95, reports < 500 ms @ 100k lines | alert on regression |

Test hygiene rules learned from the current codebase: prefer role-based queries over text; never mock the persistence module wholesale in more than one shared helper (today, adding one DataApi export breaks two mock files); anything that fixed a production data bug gets a permanent regression test named after the incident.

### 1.2 CI/CD
- GitHub Actions, trunk-based, short-lived branches, PR-only merges to `main`.
- PR pipeline: lint → typecheck → boundaries/page-budget → unit + invariant → contract diff (OpenAPI) → secret scan (gitleaks) → dependency audit.
- Main pipeline: + integration + E2E smoke → EF migrations against staging (expand-contract only) → deploy staging slot → `/health` + `/integrity` probe → swap → post-deploy probe → auto-rollback on failure.
- **No manual deploy paths remain** (the POSIX-zip ritual dies; builds on Linux runners).
- Local dev: `docker compose up` (Postgres + API) — no cloud dependency for feature work.
- Weekly: full E2E, load test, Renovate batch.

### 1.3 Code review
- Every change via PR; agent-generated code reviewed with extra suspicion on domain math and sync logic.
- CODEOWNERS: `domain/`, `Modules.Ledger`, `sync/` require explicit owner review.
- Porting PRs are split: (1) verbatim port with golden master green, (2) separate refactor PR. Never combined.
- Checklist encoded as PR template: invariants touched? migration needed? docs page updated? i18n keys added to all 5 locales?

### 1.4 Documentation
- Single VitePress site inside the monorepo (no nested git). ADRs (`docs/architecture/adr/NNN-*.md`) for every irreversible decision — the current codebase's "archaeology by code comment" is replaced by ADRs.
- `check-docs-sync` promoted from advisory to CI-enforced with a label escape hatch (`docs-not-needed` requires reviewer approval).
- Each module owns a page: purpose, invariants, commit kinds it emits, failure modes.

### 1.5 Versioning & release
- SemVer for API (`/api/v1`); breaking changes require a new version + deprecation window; the OpenAPI diff gate makes accidental breaks impossible.
- App: continuous deployment with feature flags per space; generated-client version pinned to API version (stale clients get a structured 426 upgrade response).
- Release notes generated from conventional commits; per-release `release-safety` equivalent runs in CI, artifacts stored as build artifacts, **not** committed to git.

## 2. Long-term maintainability strategy

### 2.1 Boundaries that cannot rot
- Architecture rules as executable tests (ESLint boundaries + NetArchTest) — merged into PR gate, so a violation cannot land (today's checker fails on `main` and nothing happens; that must be impossible).
- Page/file budgets with ratchet baselines that only go **down**; new god files structurally impossible.
- Contract discipline: frontend may only call the API through the generated client (`api/` folder is generated, lint-protected from edits); adding an endpoint = OpenAPI change first.

### 2.2 Naming conventions (enforced by lint where possible)
- IDs: prefixed ULIDs; commands imperative; events past tense; projections `*Projection`; ports `*Port`, adapters `*Adapter`.
- Money: `amountMinor` + `currency` only; lint rule bans arithmetic on `number` typed as money outside `domain/money`.
- i18n keys namespaced per feature; duplicate-key check stays in build (it earned its place).

### 2.3 Dependency management
- Renovate weekly, grouped; no beta/RC pins without an ADR and a removal date; patch-package only with linked upstream issue + expiry check in CI.
- Quarterly dependency review; `xlsx`-style CVE debt is not allowed to age.

### 2.4 Refactoring cadence
- "Boy-scout within budget": refactors ride feature PRs only when the budget ratchet or a test demands it; larger refactors get a one-page ADR first.
- Quarterly debt review against a living `docs/roadmap/debt-register.md` (replaces memory-file archaeology); every accepted debt has an owner and a trigger condition.
- Strangler tracking is obsolete (greenfield); instead, CI reports tier progress against [06-feature-roadmap.md](./06-feature-roadmap.md) acceptance criteria.

### 2.5 Monitoring, logging, error reporting, performance
- OpenTelemetry end-to-end; App Insights (backend SDK + frontend JS SDK via a registered log sink).
- Correlation: request/idempotency ULID as trace id from client mutation → API → SQL → SignalR ping.
- Standing alerts: integrity-hash change without a posting event (P1), 422 validation-failure spike, RLS denial occurrences (expected zero), p95 latency, 5xx rate, license-check failures, failed EF migration.
- Client error reporting with source maps; the old per-subledger sync-diagnostics panels are obsolete — replaced by one server "integrity" report.
- Performance budgets tracked per release (bundle size gate; server timings + slow-query log via App Insights/pg_stat_statements).

### 2.6 Knowledge continuity
- ADR log + module docs + incident regression tests are the institutional memory (not code comments, not chat-agent memory files).
- Runbooks in `docs/roadmap/` for: migration, rollback, space purge, key rotation, Cosmos restore — each rehearsed once per year.

## 3. What "healthy" looks like in 12 months
- Zero manual deploy steps; zero committed artifacts; one app copy; one git repo; one-command local dev.
- Every posting is an ACID transaction validated by the server and the database itself; an unbalanced ledger is physically impossible to store.
- A changing trial-balance hash without a posting event pages someone before any user notices.
- Adding a new subledger = one backend module emitting proposed entries + one feature folder — no data-layer or sync code exists to touch.
