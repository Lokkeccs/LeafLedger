using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeafLedger.Modules.Ledger.Infrastructure.Migrations;

public partial class PeriodOverlapConstraint : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE periods ADD CONSTRAINT periods_space_date_range_excl EXCLUDE USING gist (space_id WITH =, daterange(start_date, end_exclusive, '[)') WITH &&);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE periods DROP CONSTRAINT IF EXISTS periods_space_date_range_excl;");
    }
}