# ADR-0006: Preserve report tenancy with a GUC-filtered wrapper over materialized data

- **Status:** accepted
- **Date:** 2026-07-12
- **Deciders:** User (approved), LL Architect (planned), LL Docs Editor (recording)
- **Related:** P2-WP12 ([plan](../../rebuild/plans/P2-WP12-materialized-view-refresh.md)); P2-WP02 (RLS second wall and `app.current_space_id`); P2-WP07 (plain reporting views); target architecture §4.3/§8.3

## Context
P2-WP07's reporting views use PostgreSQL `security_invoker` views so the underlying table RLS policies are evaluated as the querying `leafledger_app` role. The report service binds `app.current_space_id` transaction-locally, and the RLS policies provide a second tenancy wall behind the authorization filter.

P2-WP12 materializes the hot `trial_balance` aggregation to support `REFRESH MATERIALIZED VIEW CONCURRENTLY`. PostgreSQL materialized views store a physical snapshot and do not re-evaluate the underlying tables' RLS policies when queried. Replacing the WP07 plain view with a directly readable materialized view would therefore expose rows for every space to a role that can query the relation. A stale or missing space context must also fail closed, rather than selecting the entire snapshot.

The decision is needed before the WP12 migration because it changes the mechanism of the report-read tenancy second wall. The user approved the recommended route on 2026-07-12. This is a security and architecture decision, not an accounting-rule decision; no accounting consultation or OLD golden fixture is required.

## Decision
We will keep `trial_balance` as a tenant-facing **plain `security_invoker` wrapper view** and store the aggregated snapshot in a separate physical materialized view named `trial_balance_mat`.

- `trial_balance` will select from `trial_balance_mat` only for the UUID in `NULLIF(current_setting('app.current_space_id', true), '')::uuid`.
- `trial_balance_mat` will not be granted to `leafledger_app`; callers can read only through the wrapper view.
- The wrapper will retain `security_invoker = true` as defense in depth, and the existing derived report views will continue to select from `trial_balance`.
- A missing or empty `app.current_space_id` will produce no rows. A bound space context can read only that space's materialized rows.
- The integrity endpoint and the WP08 trial-balance oracle remain on the separate always-live `trial_balance_live` view approved in P2-WP12's D-WP12-INTEGRITY decision; this ADR governs the materialized dashboard read path.

## Consequences
- **Positive:** Materialized report reads retain an explicit, fail-closed tenant boundary even though PostgreSQL does not apply RLS to the materialized view itself.
- **Positive:** The physical snapshot can be refreshed concurrently without granting direct snapshot access to application callers.
- **Positive:** The existing `balance_sheet_lines` and `income_statement_lines` view contracts can remain unchanged because they continue through `trial_balance`.
- **Neutral:** The report read path now has two relations: a physical all-space snapshot protected by database grants, and a GUC-filtered tenant wrapper. Migration and tenancy tests must verify both layers.
- **Negative:** The wrapper's explicit predicate is a security-critical duplicate of the transaction-local GUC contract. PostgreSQL does not allow this wrapper to be declared `security_invoker` while its underlying matview is ungranted to `leafledger_app`, so the wrapper relies on the explicit predicate plus the grant boundary; any future report read that bypasses the wrapper or changes the GUC semantics can reintroduce a cross-space exposure. The matview must remain ungranted.
- **Negative:** Dashboard reports may be eventually consistent while the snapshot waits for the post-commit refresh. `/integrity`, the deploy probe, and WP08 remain exact by reading `trial_balance_live` rather than the materialized dashboard path.
- **Follow-up:** P2-WP12 must test cross-space isolation, no-context fail-closed behavior, direct matview denial for `leafledger_app`, concurrent refresh, and equivalence with the live aggregation. A future multi-instance refresh mechanism is outside this ADR and remains a scale-out follow-up.

## Alternatives considered
- **Grant `leafledger_app` direct access to the materialized view and rely on the caller's RLS context.** Rejected: materialized views do not re-evaluate base-table RLS, so a valid application connection could read another space's snapshot rows.
- **Replace `trial_balance` with the materialized view without a wrapper.** Rejected for the same cross-tenant exposure and because an empty GUC would not fail closed.
- **Keep only the existing plain live view and skip materialization.** Rejected for P2-WP12: it preserves correctness and tenancy but does not address the N4 hot-report performance goal.
- **Refresh synchronously before every report or integrity read.** Rejected: it puts refresh latency back on a request path, defeats post-commit coalescing, and would compromise the exact deploy-probe path. The integrity path remains live instead.
