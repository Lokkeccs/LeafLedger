using Microsoft.EntityFrameworkCore.Migrations;

namespace LeafLedger.Modules.Ledger.Infrastructure.Migrations;

public partial class IdempotencyKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE idempotency_keys (
    space_id uuid NOT NULL,
    idempotency_key uuid NOT NULL,
    actor_id uuid NOT NULL,
    target text NOT NULL,
    request_hash bytea NOT NULL,
    response_status integer NOT NULL,
    response_body jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_idempotency_keys PRIMARY KEY (space_id, idempotency_key)
);

ALTER TABLE idempotency_keys ENABLE ROW LEVEL SECURITY;
ALTER TABLE idempotency_keys FORCE ROW LEVEL SECURITY;
CREATE POLICY p_idempotency_keys_isolation ON idempotency_keys
    USING (space_id = NULLIF(current_setting('app.current_space_id', true), '')::uuid)
    WITH CHECK (space_id = NULLIF(current_setting('app.current_space_id', true), '')::uuid);
GRANT SELECT, INSERT ON idempotency_keys TO leafledger_app;");
    migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION delete_expired_idempotency_key(p_space_id uuid, p_idempotency_key uuid)
RETURNS void
LANGUAGE sql
SECURITY DEFINER
SET search_path = public
AS $function$
    DELETE FROM idempotency_keys
    WHERE space_id = p_space_id
      AND idempotency_key = p_idempotency_key
      AND created_at < now() - interval '24 hours';
$function$;
REVOKE ALL ON FUNCTION delete_expired_idempotency_key(uuid, uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION delete_expired_idempotency_key(uuid, uuid) TO leafledger_app;");
    migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION delete_expired_idempotency_keys()
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $function$
DECLARE
    deleted_count integer;
BEGIN
    DELETE FROM idempotency_keys
    WHERE created_at < now() - interval '24 hours';
    GET DIAGNOSTICS deleted_count = ROW_COUNT;
    RETURN deleted_count;
END;
$function$;
REVOKE ALL ON FUNCTION delete_expired_idempotency_keys() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION delete_expired_idempotency_keys() TO leafledger_app;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS delete_expired_idempotency_keys(); DROP FUNCTION IF EXISTS delete_expired_idempotency_key(uuid, uuid); DROP POLICY IF EXISTS p_idempotency_keys_isolation ON idempotency_keys; DROP TABLE IF EXISTS idempotency_keys;");
    }
}