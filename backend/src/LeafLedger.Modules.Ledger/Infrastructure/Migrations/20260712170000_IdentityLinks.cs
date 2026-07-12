using Microsoft.EntityFrameworkCore.Migrations;

namespace LeafLedger.Modules.Ledger.Infrastructure.Migrations;

public partial class IdentityLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE identity_links (
    user_id uuid NOT NULL DEFAULT gen_random_uuid(),
    subject uuid NOT NULL,
    tenant_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_identity_links PRIMARY KEY (subject, tenant_id)
);

REVOKE ALL ON identity_links FROM PUBLIC;
REVOKE ALL ON identity_links FROM leafledger_app;

CREATE OR REPLACE FUNCTION resolve_identity_link(p_subject uuid, p_tenant_id uuid)
RETURNS uuid
LANGUAGE sql
SECURITY DEFINER
SET search_path = public
AS $function$
    INSERT INTO identity_links (subject, tenant_id)
    VALUES (p_subject, p_tenant_id)
    ON CONFLICT (subject, tenant_id) DO NOTHING;
    SELECT user_id
    FROM identity_links
    WHERE subject = p_subject AND tenant_id = p_tenant_id;
$function$;

REVOKE ALL ON FUNCTION resolve_identity_link(uuid, uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION resolve_identity_link(uuid, uuid) TO leafledger_app;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS resolve_identity_link(uuid, uuid); DROP TABLE IF EXISTS identity_links;");
    }
}