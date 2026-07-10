using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class AuditTriggerTests
{
    private readonly LedgerDbFixture _fixture;

    public AuditTriggerTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Insert_writes_an_audit_row_with_actor_and_after_image()
    {
        var space = await _fixture.SeedSpaceAsync();
        const string actor = "auditor-alice";
        var accountId = Guid.NewGuid();

        await using var connection = await _fixture.OpenAppAsync(space.SpaceId, actor);
        await InsertAccountAsync(connection, space, accountId, code: 1500);

        await using var cmd = new NpgsqlCommand(
            "SELECT action, actor, before, after FROM audit_log " +
            "WHERE table_name = 'accounts' AND row_id = @rid;",
            connection);
        cmd.Parameters.AddWithValue("rid", accountId);

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("INSERT", reader.GetString(0));
        Assert.Equal(actor, reader.GetString(1));
        Assert.True(await reader.IsDBNullAsync(2)); // before is null on insert
        Assert.False(await reader.IsDBNullAsync(3)); // after image present
    }

    [Fact]
    public async Task Update_writes_an_audit_row_with_before_and_after_images()
    {
        var space = await _fixture.SeedSpaceAsync();
        var accountId = Guid.NewGuid();

        await using var connection = await _fixture.OpenAppAsync(space.SpaceId, "auditor-bob");
        await InsertAccountAsync(connection, space, accountId, code: 1600);

        await using (var update = new NpgsqlCommand(
            "UPDATE accounts SET name = 'Renamed' WHERE id = @id;", connection))
        {
            update.Parameters.AddWithValue("id", accountId);
            await update.ExecuteNonQueryAsync();
        }

        await using var cmd = new NpgsqlCommand(
            "SELECT before, after FROM audit_log " +
            "WHERE table_name = 'accounts' AND row_id = @rid AND action = 'UPDATE';",
            connection);
        cmd.Parameters.AddWithValue("rid", accountId);

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.False(await reader.IsDBNullAsync(0)); // before image present
        Assert.False(await reader.IsDBNullAsync(1)); // after image present
    }

    [Fact]
    public async Task Delete_writes_an_audit_row_with_before_image_and_null_after()
    {
        var space = await _fixture.SeedSpaceAsync();
        const string actor = "auditor-carol";
        var accountId = Guid.NewGuid();

        await using var connection = await _fixture.OpenAppAsync(space.SpaceId, actor);
        await InsertAccountAsync(connection, space, accountId, code: 1700);

        await using (var delete = new NpgsqlCommand(
            "DELETE FROM accounts WHERE id = @id;", connection))
        {
            delete.Parameters.AddWithValue("id", accountId);
            await delete.ExecuteNonQueryAsync();
        }

        await using var cmd = new NpgsqlCommand(
            "SELECT action, actor, before, after FROM audit_log " +
            "WHERE table_name = 'accounts' AND row_id = @rid AND action = 'DELETE';",
            connection);
        cmd.Parameters.AddWithValue("rid", accountId);

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("DELETE", reader.GetString(0));
        Assert.Equal(actor, reader.GetString(1));
        Assert.False(await reader.IsDBNullAsync(2)); // before image present
        Assert.True(await reader.IsDBNullAsync(3));  // after is null on delete
    }

    [Fact]
    public async Task Journal_tables_produce_no_audit_rows()
    {
        var space = await _fixture.SeedSpaceAsync();
        var entryId = Guid.NewGuid();

        // Append-only journal tables are self-auditing (no audit trigger). Insert a balanced
        // entry as superuser, then assert nothing landed in audit_log for the journal tables.
        await using (var connection = await _fixture.OpenSuperuserAsync())
        await using (var tx = await connection.BeginTransactionAsync())
        {
            await using (var entryCmd = new NpgsqlCommand(
                "INSERT INTO journal_entries (id, space_id, entry_no, date, status, created_by, created_at) " +
                "VALUES (@id, @sid, 1, current_date, 'posted', @by, now());",
                connection, tx))
            {
                entryCmd.Parameters.AddWithValue("id", entryId);
                entryCmd.Parameters.AddWithValue("sid", space.SpaceId);
                entryCmd.Parameters.AddWithValue("by", Guid.NewGuid());
                await entryCmd.ExecuteNonQueryAsync();
            }

            await InsertJournalLineAsync(connection, tx, space, entryId, 1000);
            await InsertJournalLineAsync(connection, tx, space, entryId, -1000);
            await tx.CommitAsync();
        }

        await using var check = await _fixture.OpenSuperuserAsync();
        await using var countCmd = new NpgsqlCommand(
            "SELECT count(*) FROM audit_log " +
            "WHERE table_name IN ('journal_entries', 'journal_lines');",
            check);
        var auditRows = (long)(await countCmd.ExecuteScalarAsync())!;
        Assert.Equal(0, auditRows);
    }

    private static async Task InsertAccountAsync(
        NpgsqlConnection connection, SeededSpace space, Guid accountId, int code)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO accounts (id, space_id, group_id, code, name, currency, kind, is_active, created_at) " +
            "VALUES (@id, @sid, @gid, @code, 'Bank', 'CHF', 'asset', true, now());",
            connection);
        cmd.Parameters.AddWithValue("id", accountId);
        cmd.Parameters.AddWithValue("sid", space.SpaceId);
        cmd.Parameters.AddWithValue("gid", space.GroupId);
        cmd.Parameters.AddWithValue("code", code);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertJournalLineAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, SeededSpace space, Guid entryId, long amount)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO journal_lines " +
            "(id, entry_id, space_id, account_id, amount_minor, currency, base_amount_minor) " +
            "VALUES (@id, @eid, @sid, @aid, @amt, 'CHF', @base);",
            connection, tx);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("eid", entryId);
        cmd.Parameters.AddWithValue("sid", space.SpaceId);
        cmd.Parameters.AddWithValue("aid", space.AccountId);
        cmd.Parameters.AddWithValue("amt", amount);
        cmd.Parameters.AddWithValue("base", amount);
        await cmd.ExecuteNonQueryAsync();
    }
}
