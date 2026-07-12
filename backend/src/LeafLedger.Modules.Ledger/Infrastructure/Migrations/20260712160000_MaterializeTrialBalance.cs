using Microsoft.EntityFrameworkCore.Migrations;

namespace LeafLedger.Modules.Ledger.Infrastructure.Migrations;

public partial class MaterializeTrialBalance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DROP VIEW IF EXISTS income_statement_lines;
DROP VIEW IF EXISTS balance_sheet_lines;
DROP VIEW IF EXISTS trial_balance;

CREATE MATERIALIZED VIEW trial_balance_mat AS
SELECT
    jl.space_id,
    a.id AS account_id,
    a.code AS account_code,
    a.name AS account_name,
    a.kind AS account_kind,
    SUM(jl.base_amount_minor)::bigint AS base_balance_minor
FROM journal_lines jl
JOIN journal_entries je ON je.id = jl.entry_id
JOIN accounts a ON a.id = jl.account_id
WHERE je.status = 'posted'
GROUP BY jl.space_id, a.id, a.code, a.name, a.kind;

CREATE UNIQUE INDEX ux_trial_balance_mat ON trial_balance_mat (space_id, account_id);

CREATE VIEW trial_balance
AS
SELECT space_id, account_id, account_code, account_name, account_kind, base_balance_minor
FROM trial_balance_mat
WHERE space_id = NULLIF(current_setting('app.current_space_id', true), '')::uuid;

GRANT SELECT ON trial_balance TO leafledger_app;

CREATE VIEW trial_balance_live
WITH (security_invoker = true)
AS
SELECT
    jl.space_id,
    a.id AS account_id,
    a.code AS account_code,
    a.name AS account_name,
    a.kind AS account_kind,
    SUM(jl.base_amount_minor)::bigint AS base_balance_minor
FROM journal_lines jl
JOIN journal_entries je ON je.id = jl.entry_id
JOIN accounts a ON a.id = jl.account_id
WHERE je.status = 'posted'
GROUP BY jl.space_id, a.id, a.code, a.name, a.kind;

GRANT SELECT ON trial_balance_live TO leafledger_app;

CREATE VIEW balance_sheet_lines
WITH (security_invoker = true)
AS
SELECT space_id, account_id, account_code, account_name, account_kind,
    CASE WHEN account_kind = 'asset' THEN base_balance_minor ELSE -base_balance_minor END AS amount_minor
FROM trial_balance
WHERE account_kind IN ('asset', 'liability', 'equity');

GRANT SELECT ON balance_sheet_lines TO leafledger_app;

CREATE VIEW income_statement_lines
WITH (security_invoker = true)
AS
SELECT space_id, account_id, account_code, account_name, account_kind,
    CASE WHEN account_kind = 'income' THEN -base_balance_minor ELSE base_balance_minor END AS amount_minor
FROM trial_balance
WHERE account_kind IN ('income', 'expense');

GRANT SELECT ON income_statement_lines TO leafledger_app;

CREATE OR REPLACE FUNCTION refresh_trial_balance_mat()
RETURNS bigint
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $function$
BEGIN
    REFRESH MATERIALIZED VIEW CONCURRENTLY trial_balance_mat;
    RETURN (SELECT count(*) FROM trial_balance_mat);
END;
$function$;
REVOKE ALL ON FUNCTION refresh_trial_balance_mat() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION refresh_trial_balance_mat() TO leafledger_app;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DROP FUNCTION IF EXISTS refresh_trial_balance_mat();
DROP VIEW IF EXISTS income_statement_lines;
DROP VIEW IF EXISTS balance_sheet_lines;
DROP VIEW IF EXISTS trial_balance_live;
DROP VIEW IF EXISTS trial_balance;
DROP INDEX IF EXISTS ux_trial_balance_mat;
DROP MATERIALIZED VIEW IF EXISTS trial_balance_mat;

CREATE VIEW trial_balance
WITH (security_invoker = true)
AS
SELECT
    jl.space_id,
    a.id AS account_id,
    a.code AS account_code,
    a.name AS account_name,
    a.kind AS account_kind,
    SUM(jl.base_amount_minor)::bigint AS base_balance_minor
FROM journal_lines jl
JOIN journal_entries je ON je.id = jl.entry_id
JOIN accounts a ON a.id = jl.account_id
WHERE je.status = 'posted'
GROUP BY jl.space_id, a.id, a.code, a.name, a.kind;

GRANT SELECT ON trial_balance TO leafledger_app;

CREATE VIEW balance_sheet_lines
WITH (security_invoker = true)
AS
SELECT space_id, account_id, account_code, account_name, account_kind,
    CASE WHEN account_kind = 'asset' THEN base_balance_minor ELSE -base_balance_minor END AS amount_minor
FROM trial_balance
WHERE account_kind IN ('asset', 'liability', 'equity');

GRANT SELECT ON balance_sheet_lines TO leafledger_app;

CREATE VIEW income_statement_lines
WITH (security_invoker = true)
AS
SELECT space_id, account_id, account_code, account_name, account_kind,
    CASE WHEN account_kind = 'income' THEN -base_balance_minor ELSE base_balance_minor END AS amount_minor
FROM trial_balance
WHERE account_kind IN ('income', 'expense');

GRANT SELECT ON income_statement_lines TO leafledger_app;");
    }
}