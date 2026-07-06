# P1-WP05 — ADR log + foundational ADRs (online-first + Postgres; contract pipeline)

- **Phase:** 1 (foundation)
- **State:** done (QA PASS 2026-07-06)
- **Owner (implementation):** LL Docs Editor (docs-only WP; no code, tests, or config)
- **Depends on:** P1-WP03 (produced ADR-0002 — ID storage, already accepted), P1-WP04 (contract pipeline whose tooling this WP records as ADR-0003) — both done/merged
- **Estimated size:** ≤ 1 day (docs only)
- **Spec sources:** `docs/architecture/rebuild/04-implementation-plan.md` §"Phase 1" ("ADR log started (ADR-001: online-first + Postgres decision)"); `docs/architecture/rebuild/03-target-architecture.md` §"Decision (2026-07-03)" + §1 (architectural stance) + §2 (keep/drop/replace: "Dropped Dexie/IndexedDB… No local schema, no 58 migrations"; "Cosmos DB → PostgreSQL"); `docs/architecture/rebuild/05-quality-and-maintainability.md` (online-first target; contract diff gate §22/§40); `docs/rebuild/plans/NOTES-risk-review-2026-07-06.md` N1 (ID-storage ADR — already delivered as ADR-0002); `docs/rebuild/plans/P1-WP04-openapi-client-pipeline.md` line 39 ("propose ADR-0003 — API contract pipeline" deferred to P1-WP05).

## Goal
Formalize the **Architecture Decision Record (ADR) log** as the durable, reviewable record of "why the system is the way it is," and backfill the two foundational ADRs that currently have no file: **ADR-0001** (the 2026-07-03 online-first + PostgreSQL decision — the root of the whole rebuild) and **ADR-0003** (the API contract pipeline tooling chosen in P1-WP04). Register the already-accepted **ADR-0002** (ID storage) in the index. This closes the Phase-1 exit item "ADR log started" and gives every later WP a defined place and format to record decisions.

## Scope (what this WP delivers)
1. **ADR index / log** — a new `docs/architecture/adr/README.md` that: states the numbering convention (`ADR-NNNN`, zero-padded, monotonic, never reused), the status lifecycle (`proposed → accepted → superseded by ADR-XXXX`), points to `TEMPLATE.md`, and holds a **table of all ADRs** (number, title, status, date, related WP) linking each file. The index is the single entry point; `status.md` and the rebuild `README` link to it.
2. **ADR-0001 — Online-first architecture + PostgreSQL as system of record.** Records the 2026-07-03 decision that supersedes the old offline-first/Cosmos draft: the server owns all state; the client is a thin cache-aware SPA with **no local data store, no offline writes, and therefore no client-side schema/migrations** (the old "58 Dexie migrations" problem class is deleted, not solved); PostgreSQL (Flexible Server, EF Core + Npgsql, RLS) replaces Cosmos for ACID posting, FK/CHECK invariants, SQL reporting, and PITR. Status **accepted**, dated 2026-07-03 (authored 2026-07-06).
3. **ADR-0003 — API contract pipeline (build-time OpenAPI → generated TS client).** Retroactively documents the P1-WP04 decision: backend emits a committed canonical `leafledger-v1.json` at build time (`Microsoft.AspNetCore.OpenApi` + `Microsoft.Extensions.ApiDescription.Server`); the frontend generates types + a typed fetch client with `openapi-typescript` + `openapi-fetch` (hand-written TanStack Query hooks stay in the application layer — no hook codegen); CI enforces a contract-diff gate. Status **accepted**, dated 2026-07-06. Related: P1-WP04.
4. **Register ADR-0002 in the index** and resolve the stale tracker note "ADR for ID storage from P1-WP03" — it is already delivered as an accepted ADR; the index makes that visible. No change to ADR-0002's content.
5. **Cross-reference wiring** — `docs/rebuild/status.md` (P1-WP05 row + a link to the ADR index) and `docs/architecture/rebuild/README.md` (a pointer to `adr/README.md`) reference the log so it is discoverable from the doc tree's entry points.

## Non-goals (explicitly deferred)
- **No new architectural decisions.** This WP *records* decisions already made in the specs / prior WPs; it does not make new ones. Any genuinely open question surfaced while writing is raised as a plan note / proposed WP, never resolved inside an ADR here.
- **No data-migration / cutover ADR.** The launch-time **self-migration** strategy (old-app export → new-app import, read-only sunset — Part 4 §"Phase 7") is a launch decision recorded when Phase 7 is planned, not part of ADR-0001. Mention as *Related* only. (ADR-0001's "no-migration" is strictly the client-side no-local-schema consequence.)
- **No backend EF Core migration-pipeline ADR.** Backend schema migrations are in scope for Phase 2; the risk review assessed the "single migration pipeline" as already covered. Not this WP.
- **No ADR tooling / automation** (e.g., `adr-tools`, a docs link-checker CI job, markdownlint gate). The index is maintained by hand. A link-check *command* is used for local verification (AC-6) but is **not** added to CI here.
- **No edits to ADR-0002 content** beyond linking it from the index.
- **No renumbering** of existing ADRs. ADR-0002 keeps its number; ADR-0001 fills the pre-existing gap; ADR-0003 is next.

## Source material (salvage vs rewrite)
Pure documentation synthesis from the authoritative rebuild specs — **no old-code porting, no golden fixtures, no accounting rule** (per playbook §5 this WP is "rewrite/author," not "salvage"). Content is drawn verbatim-in-intent from:
- ADR-0001 ← 03 §Decision + §1 + §2 (keep/drop/replace rows for Dexie, Cosmos→Postgres, sync subsystem); 02-weaknesses.md §57–58 (the client-as-source-of-truth and numeric-id failure modes the decision eliminates).
- ADR-0003 ← the P1-WP04 plan + its Implementation notes / QA verdict (the tooling actually shipped).
- ADR template ← existing `docs/architecture/adr/TEMPLATE.md` (section set: Context / Decision / Consequences / Alternatives considered).

## Accounting / domain sign-off
- **LL Accounting Expert consult: not required.** No Swiss accounting / VAT / FX / posting-rule content — these are platform-architecture and tooling decisions. (Recorded here per repo rule; no open questions routed.)
- **Golden fixtures: N/A.** Not an accounting-rule WP; nothing to pin. Stated explicitly so QA does not flag a missing-fixtures gap.

## File list (implementation targets — LL Docs Editor)
- `docs/architecture/adr/README.md` — **new**, the ADR index/log (convention, lifecycle, table of ADRs 0001–0003).
- `docs/architecture/adr/ADR-0001-online-first-postgres.md` — **new**, foundational decision (accepted, 2026-07-03).
- `docs/architecture/adr/ADR-0003-api-contract-pipeline.md` — **new**, contract-pipeline tooling (accepted, 2026-07-06).
- `docs/architecture/adr/ADR-0002-id-storage.md` — **unchanged content**; linked from the index only.
- `docs/architecture/rebuild/README.md` — add a pointer to `../adr/README.md`.
- `docs/rebuild/status.md` — set P1-WP05 row → `done` on completion; link the ADR index; add a session-log entry.

## Acceptance criteria (verifiable checks — all must pass)
1. **Index exists and is complete.** `docs/architecture/adr/README.md` exists and its ADR table lists exactly ADR-0001, ADR-0002, ADR-0003 with the correct **status** (`accepted` for all three), **date**, **title**, and **related WP**, each linking to its file. No ADR file in the folder is missing from the table and no table row lacks a file (1:1).
2. **ADR-0001 present and spec-faithful.** `ADR-0001-online-first-postgres.md` exists, follows the template section set (Status / Date / Deciders / Related, then Context / Decision / Consequences / Alternatives considered), status = `accepted`, and its Decision states, traceably to Part 3: (a) server = single source of truth, (b) no offline writes / no client-side data store or schema-migrations, (c) PostgreSQL replaces Cosmos with the cited rationale (ACID, FK/CHECK, SQL reporting, PITR). Alternatives considered includes keeping offline-first/Cosmos, with the rejection reason.
3. **ADR-0003 present and matches shipped pipeline.** `ADR-0003-api-contract-pipeline.md` exists, follows the template, status = `accepted`, Related = P1-WP04, and its Decision names the actual tools shipped (`Microsoft.AspNetCore.OpenApi` + `Microsoft.Extensions.ApiDescription.Server`; `openapi-typescript` + `openapi-fetch`), the committed-contract + diff-gate mechanism, and the "no hook codegen — hooks stay in the application layer" boundary rationale. Alternatives considered includes orval/hook-codegen and runtime-served OpenAPI, with rejection reasons.
4. **ADR-0002 registered, unmodified.** `ADR-0002-id-storage.md` is byte-unchanged (`git diff --exit-code -- docs/architecture/adr/ADR-0002-id-storage.md` is clean after the WP) and appears in the index table with status `accepted`, related WP P1-WP03.
5. **Discoverable from entry points.** `docs/architecture/rebuild/README.md` links to `../adr/README.md`, and `docs/rebuild/status.md` links the ADR index. The stale "ADR for ID storage from P1-WP03" wording in the P1-WP05 tracker note is removed/resolved (superseded by "ADR-0002 accepted; see ADR index").
6. **No broken relative links.** Every relative Markdown link in the three new/edited ADR-area files and in the two edited entry-point files resolves to an existing path (verified by a link-existence scan, e.g. a `git ls-files`-backed grep of `](...)` targets; zero misses). Numbering is monotonic with no gaps 0001→0003 and no duplicates.
7. **Docs-only, no scope bleed.** `git diff --name-only` for the WP touches only files under `docs/architecture/adr/**`, `docs/architecture/rebuild/README.md`, and `docs/rebuild/status.md`. No code, test, config, or workflow files change (repo build/test gates are unaffected — nothing to run).

## Manual / human steps
- Optional user review of ADR-0001 wording (foundational decision) before it is marked `accepted` — the content merely restates the already-approved 2026-07-03 decision, so this is a courtesy check, not a blocker.
- No LL Accounting Expert step.

## Risks & notes
- **Scope creep into new decisions.** The chief risk of an ADR WP is *deciding* rather than *recording*. Guard: any ambiguity (e.g., exact backend migration tooling, the data-cutover strategy) is written as a proposed-WP note, not resolved in an ADR. Non-goals above name the specific temptations.
- **"No-migration" ambiguity.** The term spans two ideas — (a) client-side: no local schema / no Dexie 58-migration ceremony (a *consequence* of online-first, belongs in ADR-0001); (b) launch-time: no automated bulk data migration, users self-migrate via export/import (a *Phase-7 launch* decision, out of scope). ADR-0001 must state (a) and explicitly point to Phase 7 for (b) so the two are not conflated.
- **Numbering gap is intentional.** ADR-0002 predates ADR-0001 because ID storage was decided first in P1-WP03; ADR-0001 backfills the foundational decision. The index notes this so the ordering doesn't look like an error.
- **Retroactive `accepted` status.** ADR-0001/0003 are recorded after the fact; dates reflect when each decision was *made* (2026-07-03 / 2026-07-06), with an "authored 2026-07-06" note, mirroring ADR-0002's style.

## Implementation notes
- **2026-07-06 (LL Docs Editor) — all scope items, docs-only:**
  - Created [docs/architecture/adr/README.md](../../architecture/adr/README.md): numbering convention (`ADR-NNNN`, monotonic, never reused), status lifecycle (`proposed → accepted → superseded`), `TEMPLATE.md` pointer, and a 3-row log table (ADR-0001/0002/0003, all `accepted`, with date + related WP + links). Includes the intentional-numbering-gap note.
  - Created [ADR-0001-online-first-postgres.md](../../architecture/adr/ADR-0001-online-first-postgres.md) (status `accepted`, dated 2026-07-03 / authored 2026-07-06): server = single source of truth; no offline writes / no client-side data store or schema-migrations ("58 Dexie migrations" class deleted); PostgreSQL replaces Cosmos (ACID, FK/CHECK, RLS, SQL reporting, PITR); fresh-start launch with no bulk data migration. Alternatives: keep offline+Cosmos, online+Cosmos, hybrid, bulk ETL — all rejected with reasons.
  - Created [ADR-0003-api-contract-pipeline.md](../../architecture/adr/ADR-0003-api-contract-pipeline.md) (status `accepted`, 2026-07-06, related P1-WP04): build-time OpenAPI (`Microsoft.AspNetCore.OpenApi` + `Microsoft.Extensions.ApiDescription.Server`) → committed `leafledger-v1.json`; `openapi-typescript` + `openapi-fetch`; **no hook codegen** (hooks stay in the application layer); CI contract-diff gate. Alternatives: orval/hook-codegen, runtime-served OpenAPI, hand-maintained types, versioning lib — rejected/deferred.
  - Registered **ADR-0002** in the index (content byte-unchanged); linked the ADR log from [docs/architecture/rebuild/README.md](../../architecture/rebuild/README.md).
  - **Refinement vs plan (traceable):** the plan listed "no data-migration / cutover ADR" as a Non-goal and pinned ADR-0001's "no-migration" to the client-side no-local-schema meaning only. During implementation the rebuild README headline #5 ("No data migration (decision 2026-07-03) — fresh start") showed the no-**data**-migration launch model was **co-decided** with online-first/Postgres on 2026-07-03, not a deferrable Phase-7 detail. ADR-0001 therefore records it as part of the same foundational decision (with Phase 7 noted as its *execution*). This honors the Non-goal's intent — **no separate cutover ADR was created** — while not leaving a headline decision unrecorded. Backend EF Core migrations remain out of scope (noted as future).
  - **Verification:** all relative links in the new/edited files resolve; ADR numbering monotonic 0001→0003, no duplicates; `git diff` touches only `docs/architecture/adr/**`, `docs/architecture/rebuild/README.md`, `docs/rebuild/plans/P1-WP05-*.md`, and `docs/rebuild/status.md` (docs-only). ADR-0002 unchanged.
  - **Handoff:** ready for LL QA Reviewer (verify against ACs 1–7). Working tree carries the new/edited docs uncommitted (LL Git commits later).

## QA verdict
**PASS — 2026-07-06 (LL QA Reviewer).** All 7 acceptance criteria met; independently reproduced.

- **AC-1** — [adr/README.md](../../architecture/adr/README.md) lists exactly ADR-0001/0002/0003, all `accepted`, with title + date + related WP + resolving links. Folder holds those three + `TEMPLATE.md` (correctly excluded — not an ADR) + this index; table is 1:1 with the ADR files. PASS.
- **AC-2** — [ADR-0001](../../architecture/adr/ADR-0001-online-first-postgres.md): template section set present (Status/Date/Deciders/Related → Context/Decision/Consequences/Alternatives), status `accepted`; Decision states (a) server = single source of truth, (b) no offline writes / no client-side data store or schema-migrations, (c) PostgreSQL replaces Cosmos with cited rationale (ACID, FK/CHECK, SQL reporting, PITR); Alternatives include keeping offline-first + Cosmos with rejection reason. Content traceable to Part 3 §Decision/§1–§2 and Part 2 weaknesses. PASS.
- **AC-3** — [ADR-0003](../../architecture/adr/ADR-0003-api-contract-pipeline.md): template-compliant, `accepted`, Related = P1-WP04; Decision names the shipped tools (`Microsoft.AspNetCore.OpenApi` + `Microsoft.Extensions.ApiDescription.Server`; `openapi-typescript` + `openapi-fetch`), the committed-contract + CI diff-gate mechanism, and the no-hook-codegen boundary rationale; Alternatives include orval/hook-codegen + runtime-served OpenAPI with rejection reasons. Matches the shipped P1-WP04 pipeline. PASS.
- **AC-4** — `git diff --exit-code -- docs/architecture/adr/ADR-0002-id-storage.md` = exit 0 (byte-unchanged); ADR-0002 registered `accepted` / P1-WP03 in the index. PASS.
- **AC-5** — [rebuild/README.md](../../architecture/rebuild/README.md) links `../adr/README.md`; [status.md](../status.md) P1-WP05 row links the ADR log (`../architecture/adr/README.md`, resolves True); stale "ADR for ID storage from P1-WP03" wording removed (0 occurrences). PASS.
- **AC-6** — relative-link existence scan over the three ADR-area files + both entry-point files + this plan: `missing_links=0`. Numbering monotonic 0001→0003, no gaps, no duplicates. PASS.
- **AC-7** — `git status` changeset is docs-only: `docs/architecture/adr/{README,ADR-0001,ADR-0003}.md`, `docs/architecture/rebuild/README.md`, `docs/rebuild/status.md`, and this plan file. No code/test/config/workflow files; nothing to build or run. PASS.

**Golden fixtures / financial integrity:** correctly N/A — pure architecture docs, no money/ledger/accounting rule; SharedKernel and all code untouched. **Security:** no secrets, no endpoints, no inputs. **Hallucination scan:** every claim traces to the specs (Part 1 "Dexie v58, 45 tables"; Part 2 client-as-SoT / numeric-id failure modes; Part 3 §Decision/§1–§2/§6–§7; Part 4 Phase 1/7; rebuild README headline #5) or to the P1-WP04 deliverable — nothing invented. **Patch-layering scan:** N/A (docs).

Note (non-blocking):
- **N1 (scope refinement, accepted):** the implementer recorded the **no-data-migration / fresh-start launch** decision inside ADR-0001, whereas the approved plan's Non-goal #2 and "No-migration ambiguity" risk note had scoped it out (client-side meaning only). This is a *documented, spec-grounded* refinement — rebuild README headline #5 records "No data migration (decision 2026-07-03)" as co-decided with online-first/Postgres, so recording it in the foundational ADR improves fidelity; crucially, **no separate cutover ADR was created**, honoring the Non-goal's literal intent, and the deviation is explained in the Implementation notes. Accepted. Optional future tidy: soften the now-superseded Non-goal #2 wording so the planning intent and the delivered ADR read consistently. Not blocking.

**Verdict: PASS. State → done.** Cleared for LL Git (docs-only; commit the P1-WP05 set: `docs/architecture/adr/{README,ADR-0001,ADR-0003}.md`, `docs/architecture/rebuild/README.md`, `docs/rebuild/status.md`, `docs/rebuild/plans/P1-WP05-*.md`).
