using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace LeafLedger.Modules.Ledger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialLedgerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_name = table.Column<string>(type: "text", nullable: false),
                    row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "spaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    base_currency = table.Column<string>(type: "char(3)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spaces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_range = table.Column<NpgsqlRange<int>>(type: "int4range", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fx_policy = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_groups", x => x.id);
                    table.ForeignKey(
                        name: "FK_account_groups_account_groups_parent_id",
                        column: x => x.parent_id,
                        principalTable: "account_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_groups_spaces_space_id",
                        column: x => x.space_id,
                        principalTable: "spaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_no = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    reference = table.Column<string>(type: "text", nullable: true),
                    reverses_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_journal_entries_journal_entries_reverses_entry_id",
                        column: x => x.reverses_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_entries_spaces_space_id",
                        column: x => x.space_id,
                        principalTable: "spaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_memberships_spaces_space_id",
                        column: x => x.space_id,
                        principalTable: "spaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_exclusive = table.Column<DateOnly>(type: "date", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_periods", x => x.id);
                    table.ForeignKey(
                        name: "FK_periods_spaces_space_id",
                        column: x => x.space_id,
                        principalTable: "spaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    fx_policy = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_accounts_account_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "account_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accounts_spaces_space_id",
                        column: x => x.space_id,
                        principalTable: "spaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    base_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    fx_rate = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                    vat_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                    business_partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_journal_lines_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_lines_journal_entries_entry_id",
                        column: x => x.entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_journal_lines_spaces_space_id",
                        column: x => x.space_id,
                        principalTable: "spaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "line_attributions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_permille = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_line_attributions", x => x.id);
                    table.CheckConstraint("ck_line_attributions_share_permille", "share_permille BETWEEN 1 AND 1000");
                    table.ForeignKey(
                        name: "FK_line_attributions_journal_lines_line_id",
                        column: x => x.line_id,
                        principalTable: "journal_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_line_attributions_spaces_space_id",
                        column: x => x.space_id,
                        principalTable: "spaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_groups_parent_id",
                table: "account_groups",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_account_groups_space_id",
                table: "account_groups",
                column: "space_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_group_id",
                table: "accounts",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_space_id",
                table: "accounts",
                column: "space_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_space_id_code",
                table: "accounts",
                columns: new[] { "space_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_space_id",
                table: "audit_log",
                column: "space_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_reverses_entry_id",
                table: "journal_entries",
                column: "reverses_entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_space_id",
                table: "journal_entries",
                column: "space_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_space_id_entry_no",
                table: "journal_entries",
                columns: new[] { "space_id", "entry_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_account_id",
                table: "journal_lines",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_entry_id",
                table: "journal_lines",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_space_id",
                table: "journal_lines",
                column: "space_id");

            migrationBuilder.CreateIndex(
                name: "IX_line_attributions_line_id",
                table: "line_attributions",
                column: "line_id");

            migrationBuilder.CreateIndex(
                name: "IX_line_attributions_space_id",
                table: "line_attributions",
                column: "space_id");

            migrationBuilder.CreateIndex(
                name: "IX_memberships_space_id",
                table: "memberships",
                column: "space_id");

            migrationBuilder.CreateIndex(
                name: "IX_periods_space_id",
                table: "periods",
                column: "space_id");

            // --- Raw DDL (P2-WP02): constraints and behaviour EF cannot express. ---

            // N4 (DB half): account groups may not have overlapping code ranges within a space.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql(
                "ALTER TABLE account_groups " +
                "ADD CONSTRAINT account_groups_no_overlap " +
                "EXCLUDE USING gist (space_id WITH =, code_range WITH &&);");

            // N3 (DB half): every journal entry's base-amount lines must net to zero.
            // Deferred so a balanced entry can be inserted line-by-line inside one transaction.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION assert_entry_balanced() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    v_entry uuid;
    v_sum bigint;
BEGIN
    IF TG_OP = 'DELETE' THEN
        v_entry := OLD.entry_id;
    ELSE
        v_entry := NEW.entry_id;
    END IF;

    SELECT COALESCE(SUM(base_amount_minor), 0)
    INTO v_sum
    FROM journal_lines
    WHERE entry_id = v_entry;

    IF v_sum <> 0 THEN
        RAISE EXCEPTION 'journal entry % is unbalanced: base amount sum = %', v_entry, v_sum
            USING ERRCODE = '23514';
    END IF;

    RETURN NULL;
END;
$$;");
            migrationBuilder.Sql(@"
CREATE CONSTRAINT TRIGGER trg_journal_lines_balanced
    AFTER INSERT OR UPDATE ON journal_lines
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW
    EXECUTE FUNCTION assert_entry_balanced();");

            // Audit log: capture before/after row images for mutations of the core tables.
            // SECURITY DEFINER so the append-only audit write is independent of caller RLS/grants.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION write_audit_log() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, public AS $$
DECLARE
    v_new jsonb;
    v_old jsonb;
    v_row uuid;
    v_space uuid;
BEGIN
    IF TG_OP = 'DELETE' THEN
        v_old := to_jsonb(OLD);
        v_new := NULL;
    ELSIF TG_OP = 'INSERT' THEN
        v_old := NULL;
        v_new := to_jsonb(NEW);
    ELSE
        v_old := to_jsonb(OLD);
        v_new := to_jsonb(NEW);
    END IF;

    v_row := (COALESCE(v_new, v_old) ->> 'id')::uuid;
    v_space := COALESCE((COALESCE(v_new, v_old) ->> 'space_id')::uuid, v_row);

    INSERT INTO audit_log (id, space_id, table_name, row_id, action, actor, at, before, after)
    VALUES (gen_random_uuid(), v_space, TG_TABLE_NAME, v_row, TG_OP,
            current_setting('app.current_actor', true), now(), v_old, v_new);

    RETURN NULL;
END;
$$;");
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_spaces_audit AFTER INSERT OR UPDATE OR DELETE ON spaces
    FOR EACH ROW EXECUTE FUNCTION write_audit_log();
CREATE TRIGGER trg_memberships_audit AFTER INSERT OR UPDATE OR DELETE ON memberships
    FOR EACH ROW EXECUTE FUNCTION write_audit_log();
CREATE TRIGGER trg_account_groups_audit AFTER INSERT OR UPDATE OR DELETE ON account_groups
    FOR EACH ROW EXECUTE FUNCTION write_audit_log();
CREATE TRIGGER trg_accounts_audit AFTER INSERT OR UPDATE OR DELETE ON accounts
    FOR EACH ROW EXECUTE FUNCTION write_audit_log();
CREATE TRIGGER trg_periods_audit AFTER INSERT OR UPDATE OR DELETE ON periods
    FOR EACH ROW EXECUTE FUNCTION write_audit_log();");

            // Tenancy + immutability: least-privilege application role under row-level security.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'leafledger_app') THEN
        CREATE ROLE leafledger_app NOLOGIN;
    END IF;
END;
$$;");
            migrationBuilder.Sql(@"
GRANT SELECT, INSERT ON
    spaces, memberships, account_groups, accounts, periods,
    journal_entries, journal_lines, line_attributions, audit_log
    TO leafledger_app;");
            // Journal entries/lines/attributions and audit_log are append-only (immutability N2).
            migrationBuilder.Sql(@"
GRANT UPDATE, DELETE ON
    spaces, memberships, account_groups, accounts, periods
    TO leafledger_app;");
            migrationBuilder.Sql(@"
ALTER TABLE spaces ENABLE ROW LEVEL SECURITY;
ALTER TABLE spaces FORCE ROW LEVEL SECURITY;
CREATE POLICY p_spaces_isolation ON spaces FOR ALL TO leafledger_app
    USING (id = current_setting('app.current_space_id', true)::uuid)
    WITH CHECK (id = current_setting('app.current_space_id', true)::uuid);");
            migrationBuilder.Sql(@"
DO $$
DECLARE
    t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'memberships', 'account_groups', 'accounts', 'periods',
        'journal_entries', 'journal_lines', 'line_attributions', 'audit_log'
    ]
    LOOP
        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY;', t);
        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY;', t);
        EXECUTE format(
            'CREATE POLICY p_%s_isolation ON %I FOR ALL TO leafledger_app ' ||
            'USING (space_id = current_setting(''app.current_space_id'', true)::uuid) ' ||
            'WITH CHECK (space_id = current_setting(''app.current_space_id'', true)::uuid);',
            t, t);
    END LOOP;
END;
$$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "line_attributions");

            migrationBuilder.DropTable(
                name: "memberships");

            migrationBuilder.DropTable(
                name: "periods");

            migrationBuilder.DropTable(
                name: "journal_lines");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "journal_entries");

            migrationBuilder.DropTable(
                name: "account_groups");

            migrationBuilder.DropTable(
                name: "spaces");

            // Objects not owned by any table (functions, role, extension) dropped last,
            // after their dependent tables/policies/triggers are gone.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS write_audit_log();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS assert_entry_balanced();");
            migrationBuilder.Sql("DROP ROLE IF EXISTS leafledger_app;");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS btree_gist;");
        }
    }
}
