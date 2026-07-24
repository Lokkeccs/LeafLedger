using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeafLedger.Modules.Ledger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BusinessPartners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_partners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    partner_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_partners", x => x.id);
                    table.ForeignKey(
                        name: "FK_business_partners_spaces_space_id",
                        column: x => x.space_id,
                        principalTable: "spaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_partners_space_id_name",
                table: "business_partners",
                columns: new[] { "space_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_partners_space_id_partner_number",
                table: "business_partners",
                columns: new[] { "space_id", "partner_number" },
                unique: true,
                filter: "partner_number IS NOT NULL");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_business_partners_audit AFTER INSERT OR UPDATE OR DELETE ON business_partners
    FOR EACH ROW EXECUTE FUNCTION write_audit_log();
GRANT SELECT, INSERT, UPDATE, DELETE ON business_partners TO leafledger_app;
ALTER TABLE business_partners ENABLE ROW LEVEL SECURITY;
ALTER TABLE business_partners FORCE ROW LEVEL SECURITY;
CREATE POLICY p_business_partners_isolation ON business_partners FOR ALL TO leafledger_app
    USING (space_id = current_setting('app.current_space_id', true)::uuid)
    WITH CHECK (space_id = current_setting('app.current_space_id', true)::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS p_business_partners_isolation ON business_partners; DROP TRIGGER IF EXISTS trg_business_partners_audit ON business_partners;");
            migrationBuilder.DropTable(
                name: "business_partners");
        }
    }
}
