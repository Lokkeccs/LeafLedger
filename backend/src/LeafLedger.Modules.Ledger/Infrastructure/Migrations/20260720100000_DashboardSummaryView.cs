using Microsoft.EntityFrameworkCore.Migrations;

namespace LeafLedger.Modules.Ledger.Infrastructure.Migrations;

public partial class DashboardSummaryView : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE VIEW dashboard_summary
WITH (security_invoker = true)
AS
SELECT
    COALESCE(SUM(base_balance_minor) FILTER (WHERE account_kind = 'asset'), 0)::bigint AS total_assets_minor,
    COALESCE(SUM(-base_balance_minor) FILTER (WHERE account_kind = 'liability'), 0)::bigint AS total_liabilities_minor,
    COALESCE(SUM(-base_balance_minor) FILTER (WHERE account_kind = 'equity'), 0)::bigint AS total_equity_minor,
    COALESCE(SUM(-base_balance_minor) FILTER (WHERE account_kind = 'income'), 0)::bigint AS total_income_minor,
    COALESCE(SUM(base_balance_minor) FILTER (WHERE account_kind = 'expense'), 0)::bigint AS total_expenses_minor,
    COUNT(*)::bigint AS account_count
FROM trial_balance;

GRANT SELECT ON dashboard_summary TO leafledger_app;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS dashboard_summary;");
    }
}