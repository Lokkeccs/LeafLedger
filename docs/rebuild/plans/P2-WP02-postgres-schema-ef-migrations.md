# P2-WP02 — Postgres schema + EF migrations (spaces/accounts/groups/journal/lines/attributions/periods; RLS; deferred balance trigger; audit triggers)

- **Phase:** 2 (ledger core)
- **State:** done (QA PASS re-verify 2026-07-10 — 9/9 ACs green locally under Docker; F1–F4 closed)
- **Owner (implementation):** LL Backend Dev
- **Depends on:** P1-WP03 (SharedKernel: `Id<T>`↔uuid, `Money`/minor units, `Period`), P1-WP02 (docker-compose Postgres + Testcontainers integration tier). **Independent of P2-WP01** (fixtures pin domain behavior, not schema).
- **Blocks:** P2-WP03 (ChartOfAccounts domain maps to accounts/groups tables), P2-WP04 (Ledger domain maps to journal/lines/attributions/periods), P2-WP05 (posting endpoints write through this schema), P2-WP06 (RLS session binding sits on the policies defined here), P2-WP07 (reporting views read these tables), P2-WP08 (property suite exercises the DB walls).
- **Estimated size:** ≤ 2 days (one greenfield migration + Postgres-specific DDL + integration tests; no domain logic).
- **Spec sources:** `docs/architecture/rebuild/03-target-architecture.md` §4 (Data model: IDs/Money/Tenancy/Immutability/Audit + §4.2 core schema sketch incl. the deferred balance trigger + §4.4 concurrency/idempotency), §5 (backend structure + boundary rules), §8 (security: RLS as the second wall, immutable journal + audit); `docs/architecture/rebuild/04-implementation-plan.md` §Phase 2 ("Postgres schema for spaces/accounts/groups/journal (+ RLS, balance constraint trigger, audit triggers)") + §5 verdict (Cosmos → PostgreSQL is a **rewrite**, not a salvage); `docs/architecture/rebuild/05-quality-and-maintainability.md` §1 (Testcontainers integration tier). Risk-review [N3](./NOTES-risk-review-2026-07-06.md#n3) second wall (the DB trigger half) folds in here; the eager application check + idempotency (N2/N3 app half) stay in P2-WP05.
- **ADR-0002:** IDs stored as native `uuid`, prefix applied/stripped at the API boundary only (this WP consumes that decision; all id columns are `uuid`).

## Goal
Lay down the **authoritative Phase-2 relational schema** in PostgreSQL via EF Core migrations, with the database-enforced invariants that the old system never had: Row-Level Security tenancy isolation, a **deferred** entry-balance constraint trigger (checked at COMMIT), append-only journal immutability, audit triggers on mutable tables, and non-overlapping account-group code ranges. This WP delivers **persistence + DDL only** — no rich domain, no posting rules, no endpoints. It is the foundation every subsequent Phase-2 WP writes against.

## Scope (what this WP delivers)
1. **Tables** (all money as `bigint` minor units + `char(3)` currency; all ids `uuid`; every business table carries `space_id`):
   - `spaces(id, name, base_currency, created_at, …)`
   - `memberships(id, space_id, user_id, role, created_at, …)` — role as a typed enum/text (Owner/Admin/Member/Viewer); the AuthZ *pipeline* is WP06, this is just the table.
   - `account_groups(id, space_id, code_range int4range, name, parent_id NULL, fx_policy, …)`
   - `accounts(id, space_id, group_id FK, code, name, currency, kind, is_active, valid_from NULL, valid_to NULL, fx_policy NULL, …)`
   - `periods(id, space_id, name, start_date, end_exclusive, state, …)` — half-open range + state (`open|closed|locked`), per P1-WP03 `Period`.
   - `journal_entries(id, space_id, entry_no, date, status, description, reference, reverses_entry_id NULL FK self, created_by, created_at, …)`
   - `journal_lines(id, entry_id FK, space_id, account_id FK, amount_minor bigint, currency char(3), base_amount_minor bigint, fx_rate numeric(18,8) NULL, vat_code_id NULL, business_partner_id NULL, project_id NULL, …)` — `amount_minor` signed (+debit / −credit).
   - `line_attributions(id, line_id FK, space_id, user_id, share_permille int)` — CHECK `share_permille BETWEEN 1 AND 1000`; per-line sum = 1000 enforced by the domain in WP04 (documented here, not DB-enforced).
   - `audit_log(id, space_id, table_name, row_id, action, actor, at, before jsonb, after jsonb)`
   - `idempotency_keys` is **out of scope** → WP05 (N2 lifecycle).
2. **Row-Level Security.** `ENABLE ROW LEVEL SECURITY` + `FORCE` on every `space_id`-bearing table; a `USING`/`WITH CHECK` policy binding `space_id = current_setting('app.current_space_id')::uuid`; a non-owner application DB role (`leafledger_app`) subject to RLS; the migration/owner role bypasses for DDL. **Request-time GUC binding middleware is WP06** — this WP defines the policies + role and proves isolation by setting the GUC directly in tests.
3. **Deferred balance-constraint trigger.** `assert_entry_balanced()` → `SUM(base_amount_minor) = 0` per `entry_id`; `CREATE CONSTRAINT TRIGGER … DEFERRABLE INITIALLY DEFERRED` on `journal_lines` so it fires at COMMIT of the posting transaction. This is the **second wall** (N3); the eager application-level check is WP05.
4. **Immutability.** No `UPDATE`/`DELETE` grants to `leafledger_app` on `journal_entries`/`journal_lines` (append-only); corrections are reversal rows linked via `reverses_entry_id` (linkage column only — reversal *logic* is WP04).
5. **Audit triggers.** Row-level `BEFORE/AFTER` trigger writing `audit_log` (action, actor via `current_setting('app.current_actor')`, before/after `jsonb`) on mutable tables (`spaces`, `memberships`, `account_groups`, `accounts`, `periods`). Journal tables are their own audit record (append-only) — no audit trigger.
6. **Group code-range integrity.** `int4range` code ranges with a GiST **exclusion constraint** (`EXCLUDE USING gist (space_id WITH =, code_range WITH &&)`) preventing overlapping ranges within a space.
7. **DbContext + migrations pipeline.** One EF `LedgerDbContext` (see Decision D1) with entity type configurations, an `IDesignTimeDbContextFactory` for `dotnet ef`, a single initial migration carrying tables + all Postgres-specific DDL (RLS/triggers/exclusion via `migrationBuilder.Sql`), and a startup migrator hook (dev/CI). Persistence entities are **anemic POCOs** in `Infrastructure`.
8. **Integration tests** (Testcontainers-Postgres, `[Trait("Category","Integration")]`, run on main/local — excluded from the PR unit lane per P1-WP02).

## Non-goals (explicitly deferred)
- **No domain layer.** No aggregates, no posting-validity rules, no reversal logic, no FX resolution, no currency policy — entities are persistence POCOs only. Posting rules are WP04 (pinned by the P2-WP01 fixtures); ChartOfAccounts domain is WP03.
- **No endpoints, no idempotency middleware, no eager balance check.** All WP05.
- **No request-time RLS binding middleware / auth pipeline.** WP06. This WP only creates policies + role and sets the GUC manually in tests.
- **No reporting views / materialized views / `/integrity` hash.** WP07 (incl. N4 refresh strategy).
- **No partners / projects / VAT-code tables.** The `business_partner_id` / `project_id` / `vat_code_id` columns on `journal_lines` are created as **nullable, un-FK'd** placeholders now; their tables + FK constraints land in their owning WPs. Stated so QA does not flag "dangling FK columns."
- **No `line_attributions` sum=1000 DB enforcement.** The per-line 1000‰ sum is a domain invariant (WP04); only the per-row `1..1000` CHECK is DB-side here.
- **No data migration / seed data.** Greenfield empty schema (ADR-0001, no data migration).
- **No property/invariant suite.** WP08. This WP proves each DB wall in isolation with targeted integration tests, not generatively.

## Source material (salvage vs rewrite)
**Rewrite (Part 4 §5 verdict: Cosmos DB → PostgreSQL).** There is no schema to salvage — the old store is Dexie/Cosmos document containers; this is a fresh relational model. The **field coverage** of the old audited Dexie model (accounts incl. owners, journal entries + lines, attributions, periods, groups) is a *reference checklist* only, to ensure no field is lost; the authoritative shape is Part 3 §4.2. No behavior is ported here, so **no golden fixtures and no old-code line refs are required**.

## Decisions required (flag for user approval before implementation)
- **D1 — One DbContext now vs one-per-module.** Part 3 §5 states the target is "one DbContext per module over a shared database, single migration pipeline." The Phase-2 tables are tightly coupled (cross-entity FKs `journal_lines → accounts → account_groups`; `journal_entries.space_id → spaces`; the balance trigger spans `journal_lines`), and the owning **domain modules do not exist yet** (WP03/WP04 build them). **Recommendation:** implement WP02 with a **single `LeafLedger.Modules.Ledger` `LedgerDbContext`** owning the full baseline migration, and split out `Spaces`/`ChartOfAccounts` contexts when those modules gain domains (WP03+) — a mechanical re-home on a greenfield DB (no data). This is a deviation from Part 3 §5's per-module rule for the schema-baseline WP only; **candidate ADR-0004** ("single migration context for the Phase-2 baseline; per-module split deferred"). *User: confirm D1 or direct the per-module split now.*
- **D2 — Balance invariant currency.** Spec sketch says `SUM(base_amount_minor) = 0 per entry` (base/reporting currency, not transaction currency). Plan adopts base currency verbatim from Part 3 §4.2. *No accounting consult required — pinned by the spec.*

## Accounting / domain sign-off
- **LL Accounting Expert consult: not required.** This WP encodes schema shape + invariants directly from Part 3 §4; it makes no accounting-rule interpretation (posting rules, currency policy, FX are WP03/WP04, already pinned by P2-WP01 fixtures + the spec).
- **Golden fixtures: not applicable.** No behavior is ported. The DB invariants are verified by the integration tests below and (generatively) by the WP08 property suite. Stated so QA does not flag a "missing fixtures" prerequisite.

## Dependencies to add (per repo policy — record before adding)
- `Npgsql.EntityFrameworkCore.PostgreSQL` (EF Core provider) — `LeafLedger.Modules.Ledger`.
- `Microsoft.EntityFrameworkCore.Design` — `LeafLedger.Modules.Ledger` (design-time only) + referenced by an EF tooling target.
- (Test project already has `Testcontainers.PostgreSql`, `Npgsql`, `xunit` from P1-WP02.)
No other new dependencies.

## File list (target)
**New — `backend/src/LeafLedger.Modules.Ledger/`**
- `LeafLedger.Modules.Ledger.csproj`
- `Infrastructure/LedgerDbContext.cs`
- `Infrastructure/DesignTime/LedgerDbContextFactory.cs` (`IDesignTimeDbContextFactory`)
- `Infrastructure/Entities/{Space,Membership,AccountGroup,Account,Period,JournalEntry,JournalLine,LineAttribution,AuditLogEntry}.cs` (anemic POCOs)
- `Infrastructure/Configurations/{Space,Membership,AccountGroup,Account,Period,JournalEntry,JournalLine,LineAttribution,AuditLog}Configuration.cs`
- `Infrastructure/Migrations/*_InitialLedgerSchema.cs` (EF-generated tables + `migrationBuilder.Sql(...)` for RLS policies, roles, balance trigger fn+trigger, audit trigger fn+triggers, GiST exclusion constraint, app-role grants/immutability revokes)
- `Infrastructure/Sql/{rls_policies,balance_trigger,audit_trigger,grants}.sql` (raw DDL invoked by the migration; optional but preferred for reviewability)

**Modified**
- `backend/LeafLedger.sln` (add the module project)
- `backend/src/LeafLedger.Host/Program.cs` (register `LedgerDbContext` via Npgsql from `appsettings` connection string; apply migrations on startup in Dev/CI)
- `backend/src/LeafLedger.Host/LeafLedger.Host.csproj` (reference the module)
- `backend/tests/LeafLedger.ArchitectureTests/ModuleBoundaryTests.cs` (add `LeafLedger.Modules.Ledger` to `AllLeafLedgerAssemblies()`; EF-in-Infrastructure rule now bites)
- `backend/tests/LeafLedger.IntegrationTests/LeafLedger.IntegrationTests.csproj` (reference the module)

**New — `backend/tests/LeafLedger.IntegrationTests/`**
- `Ledger/SchemaMigrationTests.cs`
- `Ledger/RlsTenancyTests.cs`
- `Ledger/BalanceTriggerTests.cs`
- `Ledger/ImmutabilityTests.cs`
- `Ledger/AuditTriggerTests.cs`
- `Ledger/GroupCodeRangeTests.cs`
- `Ledger/LedgerDbFixture.cs` (shared Testcontainers-Postgres fixture: start container, apply migrations, expose `leafledger_app`-role and owner-role connection strings, helper to set `app.current_space_id`/`app.current_actor`)

## Acceptance criteria (concrete, independently verifiable)
Backend build + arch gates:
1. **Build & boundaries green.** `dotnet build -c Release` = 0 warnings/0 errors; `dotnet test … --filter "Category!=Integration"` runs the arch suite green — `ModuleBoundaryTests` now includes `LeafLedger.Modules.Ledger` and proves: SharedKernel depends on no module; `*.Domain` (none yet) unaffected; **EF Core resides only in `*.Infrastructure`** (the DbContext/configs are all under `Infrastructure`).

Integration tier (Testcontainers, `Category=Integration`):
2. **Migration applies to an empty DB & is model-consistent.** `SchemaMigrationTests`: applying all migrations to a fresh container succeeds; `context.Database.HasPendingModelChanges()` is `false` (model ≡ migrations); all expected tables + the `audit_log` table exist (queried from `information_schema`).
3. **RLS isolates tenants.** `RlsTenancyTests`: with `app.current_space_id` = space A on a `leafledger_app` connection, a `SELECT` over each space-scoped table returns **only** space-A rows; an `INSERT`/`SELECT` targeting space B's id returns 0 rows / is rejected by the policy `WITH CHECK`. With no GUC set, queries return 0 rows (fail-closed). Owner/migration role is unaffected.
4. **Deferred balance trigger — rejects unbalanced at COMMIT.** `BalanceTriggerTests`: a direct-SQL transaction inserting `journal_lines` for one entry whose `SUM(base_amount_minor) ≠ 0` **commits successfully mid-transaction** (deferred) but the **COMMIT throws** (constraint violation); a balanced entry (`SUM = 0`) commits cleanly. Proves the second wall independent of any application check (N3, integration half).
5. **Journal immutability.** `ImmutabilityTests`: as `leafledger_app`, `UPDATE` and `DELETE` on `journal_entries` and `journal_lines` are **denied** (insufficient privilege); `INSERT` is allowed. A reversal row referencing `reverses_entry_id` inserts successfully (linkage column works).
6. **Audit trigger captures mutations.** `AuditTriggerTests`: an `UPDATE` to an `accounts` row (with `app.current_actor` set) writes exactly one `audit_log` row with `action='UPDATE'`, correct `actor`, and `before`/`after` jsonb reflecting the change; an `INSERT`/`DELETE` likewise. Journal tables produce **no** audit_log row (append-only, self-auditing).
7. **Group code ranges cannot overlap.** `GroupCodeRangeTests`: inserting two `account_groups` in the same space with overlapping `int4range` code ranges is rejected by the GiST exclusion constraint; non-overlapping ranges (and identical ranges in *different* spaces) succeed.
8. **Money/id typing enforced by schema.** `SchemaMigrationTests` asserts (via `information_schema.columns`): all amount columns are `bigint`; currency columns are `char(3)`/`character(3)`; all `id`/`*_id` columns are `uuid`; there are **no** `real`/`double precision`/`money`/`numeric` columns on amount fields (`fx_rate` is the only `numeric` and is a rate, not an amount).

CI:
9. **Integration tests excluded from the PR unit lane, run on main** (P1-WP02 pattern preserved): the `contract`/backend PR jobs stay green without a Docker engine; the integration tests execute in the main-branch Testcontainers job.

## Definition of done
All 9 acceptance criteria pass; `dotnet build -c Release` clean; arch suite green with the new module included; integration suite green under Testcontainers; no new dependencies beyond the two listed; D1 confirmed by the user (and ADR-0004 drafted if the single-context recommendation is accepted). Then → verify (LL QA Reviewer).

## Implementation notes
_(dated; deviations from the plan and why)_

- **2026-07-10 — implemented; state → verify (LL Backend Dev).**
  - **Module scaffolded.** New `backend/src/LeafLedger.Modules.Ledger/` project: 9 anemic POCO entities + 9 `IEntityTypeConfiguration<T>` (all `internal sealed`, under `Infrastructure`), `LedgerDbContext` (`public sealed`), `IDesignTimeDbContextFactory`, and a `LedgerModule` DI extension (`AddLedgerModule`/`MigrateLedgerAsync`). Added to `LeafLedger.sln` under `src`; D1 confirmed by user → single `LedgerDbContext` baseline.
  - **Deviation — inline SQL over `Infrastructure/Sql/*.sql`.** The plan's file list listed optional external `.sql` files ("optional but preferred"). Implemented all Postgres-specific DDL (btree_gist extension, GiST exclusion constraint, deferred balance constraint trigger, audit fn+triggers, `leafledger_app` role + grants, RLS enable/force/policies) inline via `migrationBuilder.Sql(...)` in the single `InitialLedgerSchema` migration. Keeps the DDL versioned with the migration and reviewable in one place; no separate embedded-resource plumbing.
  - **Deviation — EF/Npgsql version alignment (dependency records).** `AspNetCore.HealthChecks.NpgSql` 9.0.0 transitively pulls EF Core **9.0.1**, colliding (NU1605/MSB3277 under `TreatWarningsAsErrors`) with the module's provider-driven EF **9.0.4**. Resolved by (a) bumping `Npgsql` 8.0.3 → **9.0.3** in Host + IntegrationTests, and (b) a **package-level** pin of `Microsoft.EntityFrameworkCore.Relational` **9.0.4** in Host, IntegrationTests, and ArchitectureTests. The Host pin is package-graph only — the Host takes **no EF IL dependency** (all EF calls are inside the module's `Infrastructure`), so `EfCoreIsConfinedToInfrastructureNamespaces` stays green.
  - **Snake_case columns.** `OnModelCreating` applies a deterministic `ToSnakeCase` over every property (no `EFCore.NamingConventions` dependency) so the raw DDL identifiers (`space_id`, `code_range`, `base_amount_minor`, …) match the EF model exactly.
  - **Analyzer accommodations.** Added `Infrastructure/Migrations/.editorconfig` (`generated_code = true`) so the scaffolded migration's constant-array args don't trip CA1861 under `TreatWarningsAsErrors`; added `<NoWarn>$(NoWarn);CA1707</NoWarn>` to IntegrationTests (repo convention for underscored xUnit names, matching SharedKernel.Tests); collection marker named `LedgerDbCollectionDefinition` (avoids CA1711).
  - **Audit scope.** Audit triggers on the 5 mutable tables (`spaces`, `memberships`, `account_groups`, `accounts`, `periods`) per plan §5; journal tables intentionally un-audited (append-only, self-recording). Audit fn is `SECURITY DEFINER` with a fixed `search_path` so the append-only write is independent of caller RLS/grants.
  - **Host wiring.** `Program.cs` calls `AddLedgerModule(connectionString)` (guarded on a present connection string) and, in Development only, `await app.Services.MigrateLedgerAsync()`.
  - **Results.** `dotnet build LeafLedger.sln` = **0 warnings / 0 errors**; `dotnet test --filter "Category!=Integration"` = **arch 3/3 + unit 45/45 pass** (boundary rule holds with the module added). Integration suite (`Ledger/` — 6 classes over Testcontainers `postgres:17`) **compiles clean** but cannot run locally (no Docker engine on this machine) → **CI-verified**, following the P1-WP02 precedent.
  - **GUC contract reaffirmed.** Policies read `app.current_space_id`; audit reads `app.current_actor`. WP06 (RLS binding middleware) and WP05 (actor stamping) must set these.

- **2026-07-10 — QA findings F1–F4 addressed; Docker installed; full suite run locally (LL Backend Dev).**
  - **Docker Desktop 4.81 installed** (winget, arm64) and started — the integration tier now runs on this machine (engine `linux/aarch64`, image `postgres:17`), so ACs 2–8 were executed locally rather than CI-only.
  - **F1 (AC3 — no-GUC fail-closed).** Added `RlsTenancyTests.App_role_with_no_space_context_is_fail_closed`: a `leafledger_app` connection with no `app.current_space_id` sees 0 rows and its write is rejected (`42501`). New fixture helper `OpenAppNoContextAsync()` uses a **non-pooled** connection — Npgsql resets `SET ROLE` but not a `set_config()` GUC on pool return, so a pooled connection leaked a prior test's space id and masked the fail-closed path; the dedicated connection makes "no context" deterministic.
  - **F2 (AC5 — reversal linkage).** Added `ImmutabilityTests.App_role_can_insert_a_reversal_entry_linked_via_reverses_entry_id`: as `leafledger_app`, an INSERT of a new entry with `reverses_entry_id` → the seeded original (+ balanced lines) commits, and the linkage column reads back. Proves app-role INSERT is allowed and the self-FK works.
  - **F3 (AC6 — DELETE audit + journal-no-audit).** Added `AuditTriggerTests.Delete_writes_an_audit_row_with_before_image_and_null_after` (action `DELETE`, actor, before present, after null) and `Journal_tables_produce_no_audit_rows` (a balanced journal insert leaves `audit_log` empty for `journal_entries`/`journal_lines`).
  - **F4 (AC8 — negative amount-typing + all-uuid).** Added to `SchemaMigrationTests`: `No_amount_column_uses_a_float_or_money_type` (zero `real`/`double precision`/`money` columns), `Fx_rate_is_the_only_numeric_column` (single `numeric` = `journal_lines.fx_rate`), `Every_id_column_is_uuid` (all `id`/`*_id` columns are `uuid`), and an `Expected_table_exists` theory enumerating all 9 tables (closes the AC2 note).
  - **Results.** `dotnet build -c Release` = **0/0**; `dotnet test LeafLedger.sln -c Release` (all tiers) = **unit 45/45 + arch 3/3 + integration 37/37 green** (integration was 21 → 37 with the added coverage). No production DDL/config changed — findings were pure test additions.

## QA verdict

**2026-07-10 — FAIL (LL QA Reviewer). State → in-progress.**

Reproduced locally (verifiable without Docker):
- `dotnet build LeafLedger.sln -c Release` = **0 warnings / 0 errors**.
- `dotnet test -c Release --filter "Category!=Integration"` = **arch 3/3 + unit 45/45 PASS** — including `EfCoreIsConfinedToInfrastructureNamespaces` with `LeafLedger.Modules.Ledger` now in the assembly set (AC1 met; Host carries no EF IL).
- Change scope matches the plan file list (module + `Ledger/` integration tests + sln/Host/arch/integration wiring); no unrelated files. Documented deviations (inline SQL, EF `Relational` 9.0.4 pins, Migrations `.editorconfig`, `CA1707` NoWarn) are reasonable and do not leak EF into Host IL.
- DDL/config review: money = `bigint` minor units, currency `char(3)`, `fx_rate numeric(18,8)` (rate, not amount), ids `uuid`; FKs entry→Cascade / space+account→Restrict; deferred constraint trigger sums `base_amount_minor` per entry; audit fn `SECURITY DEFINER` with pinned `search_path`; least-privilege `leafledger_app` (NOLOGIN) withholds UPDATE/DELETE on the four append-only tables; RLS ENABLE+FORCE + per-table policies on `app.current_space_id` (spaces keyed on `id`); btree_gist supports `uuid` on pg17 → exclusion constraint valid. No secrets; no patch-layering (triggers raise explicit SQLSTATEs; tests assert specific `SqlState`, not broad catches).

Blocking findings — explicitly-stated AC sub-clauses with **no covering test** (QA cannot assume untested behavior; QA does not write the tests):
1. **AC3 (security) — no-GUC fail-closed untested.** `backend/tests/LeafLedger.IntegrationTests/Ledger/RlsTenancyTests.cs` covers GUC-set isolation and cross-tenant-insert rejection (42501) but has no case opening a `leafledger_app` connection with **no** `app.current_space_id` set and asserting `SELECT count(*) = 0` (and writes rejected) on space-scoped tables. Expected per AC3 ("With no GUC set, queries return 0 rows (fail-closed)"); actual: absent. Add the fail-closed test.
2. **AC5 — reversal linkage untested.** `.../Ledger/ImmutabilityTests.cs` proves UPDATE/DELETE denial but never inserts a `journal_entries` row with `reverses_entry_id` referencing an existing entry (AC5 final clause: "A reversal row … inserts successfully (linkage column works)"), and app-role `INSERT`-allowed is not shown (seed inserts run as superuser). Add an app-role reversal-insert success test.
3. **AC6 — audit scope half-covered.** `.../Ledger/AuditTriggerTests.cs` covers INSERT + UPDATE image capture but (a) has no `DELETE` case and (b) never asserts that mutating a **journal** table produces **no** `audit_log` row (the append-only/self-auditing decision in AC6 + notes §Audit scope is unproven). Add a DELETE-audit assertion and a journal-no-audit assertion.
4. **AC8 (money integrity) — negative typing guard missing.** `.../Ledger/SchemaMigrationTests.cs` asserts specific `bigint`/`char(3)`/`uuid` columns but not AC8's core guard: "no `real`/`double precision`/`money` amount columns (`fx_rate` the only `numeric`)" nor "all `*_id` columns are `uuid`". Add an `information_schema` query asserting zero float/money amount columns and every `*_id` column is `uuid`.

Non-blocking (tighten when addressing the above): AC2 does not explicitly enumerate all 9 tables exist (relies on `HasPendingModelChanges`); AC5 tests one op per journal table rather than UPDATE+DELETE on both; AC6 reads the first audit row rather than asserting "exactly one".

Environment note (not itself a FAIL reason): integration ACs 2–8 could not be executed locally — no Docker engine on the review machine. Per the P1-WP02 precedent they are CI-verified; combined with findings 1–4, the Testcontainers suite must be re-run green in CI after the added tests before this WP returns to `verify`/`done`.

Route: LL Backend Dev to add the four missing integration tests (F1–F4) — DDL/config unchanged expected. Re-run the `Ledger/` suite in CI, then resubmit for QA.

**2026-07-10 — PASS (LL QA Reviewer, re-verify). State → done.**

Docker Desktop is now installed locally, so all nine ACs were executed (not just statically reviewed).
- **Scope:** the fix round touched only test files (`Ledger/` integration tests + `LedgerDbFixture`); tracked `backend/src` diff is unchanged (Host wiring only); the production module is untracked and byte-identical to the reviewed state. Claim "pure test additions" confirmed.
- **Reproduced:** `dotnet build -c Release` = **0/0**; `dotnet test LeafLedger.sln -c Release` = **unit 45/45 + arch 3/3 + integration 37/37** (integration 21 → 37).
- **Findings closed (each re-run by name, PASS):** F1 `RlsTenancyTests.App_role_with_no_space_context_is_fail_closed` (0 rows + `42501` write reject with no GUC); F2 `ImmutabilityTests.App_role_can_insert_a_reversal_entry_linked_via_reverses_entry_id`; F3 `AuditTriggerTests.Delete_writes_an_audit_row_with_before_image_and_null_after` + `Journal_tables_produce_no_audit_rows`; F4 `SchemaMigrationTests.No_amount_column_uses_a_float_or_money_type` + `Fx_rate_is_the_only_numeric_column` + `Every_id_column_is_uuid` + `Expected_table_exists` (×9 tables). The non-pooled `OpenAppNoContextAsync` fix is correct and test-only.
- **AC coverage now complete:** AC1 build+boundaries; AC2 migration applies + no pending model changes + 9 tables enumerated; AC3 isolation + cross-tenant reject + no-GUC fail-closed; AC4 deferred balance trigger (`BalanceTriggerTests`); AC5 UPDATE/DELETE denied + reversal insert; AC6 INSERT/UPDATE/DELETE audit + journal-no-audit; AC7 code-range exclusion; AC8 full money/id typing incl. negative guard; AC9 integration excluded from the unit lane. Security/financial/patch-layering re-checks unchanged (sound). No secrets.
- **Carry-forward (non-blocking, WP06):** the F1 root cause — a pooled connection retaining `app.current_space_id` set via `set_config(..., false)` — is a **production-relevant** risk. WP06's RLS session-binding middleware must set the space GUC on **every** request (or use transaction-local `set_config(..., true)`) and never assume a clean pooled connection, else a request could inherit a prior tenant's context. Recorded in Open questions.

Verdict: **PASS → done.** Cleared for LL Git.

## Open questions / notes for later WPs
- **N-D1** ADR-0004 (single-context baseline) to be written by LL Docs Editor once D1 is confirmed.
- **N-WP06-pool** RLS binding middleware (WP06) must set `app.current_space_id` per request (or transaction-local), because a pooled Postgres connection retains a session GUC set via `set_config(..., false)` — surfaced by the P2-WP02 fail-closed test (Npgsql resets `SET ROLE` but not the custom GUC). Fail-closed prevents leakage only when the GUC is truly unset.
- The `app.current_space_id` / `app.current_actor` GUC names defined here are the contract WP06 (RLS binding middleware) and WP05 (actor stamping) must honor — recorded so those WPs don't reinvent them.
- `idempotency_keys` table + 24 h TTL cleanup (N2) intentionally deferred to WP05.
