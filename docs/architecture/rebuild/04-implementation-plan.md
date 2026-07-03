# LeafLedger Rebuild Analysis — Part 4: Implementation Plan & Porting Strategy (Greenfield, Online-First + PostgreSQL)

> Companion to [03-target-architecture.md](./03-target-architecture.md). Supersedes the earlier hybrid/offline-first plan following the 2026-07-03 decision to drop offline writes and Cosmos. Durations are effort estimates for ~1–2 developers assisted by agents; treat as relative sizing.
>
> **What changed vs the old plan:** the entire "Phase 3 sync foundation" critical path (commit store, outbox, multi-device convergence harness) is deleted — that work no longer exists. Additionally (decision 2026-07-03): **no data migration from Cosmos** — the new system launches empty; existing users self-migrate via CSV export/import. Net effect: ~6 months → **~3.5–4 months**, with a structurally simpler end state.

## 1. Phased plan

### Phase 0 — Stop the bleeding (in the CURRENT codebase, immediately)
Unchanged and still urgent — the old system stays live during the whole rebuild:
1. Lock down `/maintenance/reset-all-data` + `/maintenance/erase-all-data` (env gate + platform-admin check). **First.**
2. Wire existing guard scripts (lint, typecheck, boundaries, tests) into GitHub Actions on the old repo.
3. Fix the one boundary violation so the gate can be strict.
4. **Verify export coverage** in the old app (accounts incl. owners, journal entries, master data as CSV/XLSX) — exports are how users self-migrate; close gaps now.
5. Authoritative Cosmos + blob backup; archive/freeze `Accounting_docs/`.

### Phase 1 — New repo + foundations (2 wk)
Monorepo (`app/`, `backend/`, `docs/` un-nested, `tools/`); CI skeleton with every gate blocking from day one; docker-compose local dev (Postgres + API); SharedKernel (Money in minor units, ULIDs, Period, Result); OpenAPI → TS client generation pipeline; ADR log started (ADR-001: online-first + Postgres decision).

### Phase 2 — Ledger core (4 wk) ← the critical path now
Postgres schema for spaces/accounts/groups/journal (+ RLS, balance constraint trigger, audit triggers); Ledger + ChartOfAccounts domain in C# (posting rules ported from `postingValidity.ts`/`fxPolicy.ts` semantics, float tolerances → exact integer math); posting/reversal/period endpoints; idempotency middleware; auth pipeline (Entra → license → role → permission). Exit criterion: **property-based invariant suite green** — any generated command sequence yields trial balance ≡ 0, unbalanced/closed-period/invalid-ref posts rejected with 422, retried posts exactly-once.

### Phase 3 — Frontend re-platform (3 wk, overlaps P2 tail)
New app shell: MSAL (sessionStorage), generated client, TanStack Query conventions, error boundaries, i18n corpus imported wholesale; port `shared/` UI primitives; accounts + journal entry pages against the new API (decomposed under page budgets, single posting flow). Exit: post → see it in trial balance report, two browsers live-update via SignalR ping.

### Phase 4 — Feature porting (6–8 wk)
Scope tiers defined in [06-feature-roadmap.md](./06-feature-roadmap.md) (M1 first half, M2 second half). In dependency order, each landing with its golden-master parity test: periods/close + FX revaluation → VAT (codes, report) → import pipeline (parsing/automation logic ported; staging becomes **server-side**; booking = posting API) → subledgers (real estate, investments — generators emit proposed entries) → budgets + dashboards (SQL views; kills derived-summary drift) → admin/spaces/members/licensing UI against server-authoritative roles.

### Phase 5 — Integrations & product upgrades (3 wk)
Market data module (FX + prices hosted services, ports/adapters); attachments (SAS grant port); **Documents module: Azure AI Document Intelligence OCR + Swiss QR-bill extraction → prefilled entry proposals** (replaces tesseract.js, upgrades the product); Integrations stub (KNX/webhooks, camt.053 groundwork) behind its one-way boundary.

### Phase 6 — QA hardening (2–3 wk, overlaps P4–P5)
Golden masters for 3 reference ledgers (FX-heavy business, simple personal, shared multi-user); ~15 Playwright journeys; load test (100k-line space: reports < 500 ms, posting < 300 ms p95); accessibility pass; beta cohort on fresh spaces (self-imported via CSV where desired — doubling as a real-world test of the import pipeline).

### Phase 7 — Launch & sunset (1–2 wk)
Open the new app (internal → beta → all); publish a self-migration guide (export from old → import to new); old stack switches to **read-only** (posting disabled, exports enabled) at sunset start; after the sunset window, decommission Cosmos + old App Service and archive the old repos.

## 2. Timeline (Gantt-style)

```
Week:        1  2  3  4  5  6  7  8  9 10 11 12 13 14 15 16
P0 Stabilize ██
P1 Repo      ████
P2 Ledger        ████████            ← critical path
P3 Frontend          ░░██████
P4 Features                 ████████████████
P5 Integr.                              ██████
P6 QA                        ░░░░░░░░░░██████
P7 Launch                                    ████
```
Dependencies: P2 blocks P3/P4; P4 and P5 overlap; P7 needs P6 green (M1 launch can precede M2 completion per the adoption waves in [06-feature-roadmap.md](./06-feature-roadmap.md)). Total ≈ **3.5–4 months**.

## 3. Module dependency graph

```
SharedKernel ◄── everything
ChartOfAccounts ◄── Ledger ◄── Subledgers, Imports, Budgets(views)
Spaces&Identity ◄── all modules (tenancy) ; Licensing ◄── middleware
MarketData ◄── Ledger(FX), Subledgers ; Attachments ◄── Imports, Ledger
Documents(OCR/QR) ─► Imports ; Integrations(KNX/bank) ─► Imports   (one-way, nothing depends on them)
Frontend: features → application(TanStack Query) → api(generated) — no other path
```

## 4. Risks & mitigations

| # | Risk | L×I | Mitigation |
|---|---|---|---|
| 1 | Fresh start churns existing users (re-entry burden, lost history access) | High×High | Export-coverage check in Phase 0; polished self-migration guide + import assist; old stack read-only (not gone) through a generous sunset window; opening-balance import path so users can start clean without full history |
| 2 | Domain port drifts porting TS → C# (subtle FX/VAT/rounding differences) | High×High | Shared golden fixtures executed by BOTH old TS and new C# implementations; divergence must be explained in an ADR or fixed — see §5 fidelity protocol |
| 3 | Second-system effect / scope growth | High×High | Golden masters define "done"; anything without an old-behavior test is out of v1 |
| 4 | Losing offline capability angers real users | Med×Med | Verify with usage data before P3 (how many posts happen offline today?); mitigations: read-only offline cache, server-side import staging (survives device loss — an upgrade); if demand is proven later, add server-drafts, never replication |
| 5 | Old system decays during rebuild | Med×High | Phase 0 CI + endpoint lockdown; only S1/S2 fixes on old code |
| 6 | Postgres/EF unfamiliarity vs Cosmos habits | Med×Med | Testcontainers integration tests from P1; migrations reviewed like code; RLS verified by dedicated tests |
| 7 | Single-developer bus factor / agent hallucination | Med×High | CI gates + property tests + golden masters; ADRs record intent; see [07-vibe-coding-playbook.md](./07-vibe-coding-playbook.md) |

## 5. Porting strategy

**Salvage (port with tests):**
- Domain semantics: `postingValidity` rules, `fxPolicy`, `periodCloseEngine`, `fxRevaluationEngine`, VAT logic, attribution derivation, account-group definitions/code ranges, `pivotEngine` math → **C# domain** (authoritative), with a thin TS mirror for UX pre-validation. ~120 of 175 old test files convert into the fidelity oracle (fixtures extracted, assertions re-expressed against both implementations).
- Import parsing + automation rules (mature, well-tested) → server-side Imports module; column-mapping UX ports as-is.
- Subledger journal generators (investment lots, real-estate flows) → proposed-entry generators.
- React UI primitives, feature-policy matrix, i18n corpus, MSAL flow shape, blob SAS service (C# ports nearly verbatim), docs content.

**Rewrite:**
- All data access (Dexie → TanStack Query + generated client); journal/import pages (decomposed); roles/permissions; licensing enforcement; maintenance/admin operations; OCR (Document Intelligence).

**Discard:**
- Entire sync subsystem and its 11 patch layers; `db.ts`; Cosmos repositories, seq allocation, schema-tolerance queries, `EntityValidator`; service-worker data logic; OPFS; `Accounting_docs/`; committed `publish/`, `temp-*.py`, artifacts; dead license function.

**Fidelity protocol (anti-hallucination):**
1. Before porting a unit: extract its old tests + capture golden-master outputs from the *old running implementation* (fixture in → artifact out).
2. Port **verbatim in intent**; run fixtures against the new code. Divergence = ADR (deliberate fix) or bug.
3. Agents port first, refactor in a separate reviewed commit with tests already green — never combined.
4. Whole-ledger cross-check: reference ledgers posted through old and new systems → identical trial balance, BS, P&L, VAT report **to the cent**.

## 6. Rollback plan

- **During rebuild:** old system untouched and live; rollback = do nothing.
- **During transition:** old stack stays available read-only through the sunset window — a user unhappy in the new app still has full access to their old data and exports; nothing was migrated, so nothing can be corrupted by rollback.
- **Post-launch:** Postgres PITR (35 d) + daily logical dumps; immutable journal + audit log allow reconstruction of any state; blob soft-delete.
- **Deploy-level:** App Service slot swap back + Vercel instant rollback; `/integrity` probe gates every deploy; EF migrations are expand-contract (no destructive step in the same release that depends on it).

## 7. Strategy variants (updated for the online-first decision)

- **7.1 Minimum viable launch (~2.5–3 months):** Phases 0–3 + M1 scope only ([06-feature-roadmap.md](./06-feature-roadmap.md)); launch to new users and M1-covered switchers while M2 features are still being ported. Viable — the fresh-start decision makes this cheap since no per-space migration coordination exists.
- **7.2 Full greenfield (recommended, ~3.5–4 months):** the plan above, launching after M1 with M2 following within weeks.
- **7.3 Fix-in-place:** superseded — dropping Dexie-as-truth forces rewriting all 36+ DataApis anyway, which removes the main argument for staying in the old repo.

## 8. Final recommendations

1. **Phase 0 is still this week's job** — the maintenance endpoints and missing CI protect the system you'll be running for the next 4+ months.
2. **Go full greenfield (7.2).** Online-first + Postgres shrinks the rebuild below the threshold where hybrid staging pays off.
3. **Let the database enforce the ledger:** balance trigger, FKs, RLS, immutable journal — invariants the old architecture could never express.
4. **Golden fixtures are the spine of the project:** porting oracle and regression suite in one artifact set. Build them before porting anything.
5. **Ship one visible product win with the rebuild** (Swiss QR-bill scanning via Document Intelligence) so the migration lands as an upgrade, not a re-platform.
6. **Validate the offline assumption with data before P3** (risk #4) — it's the one decision here that's expensive to reverse.
