using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class ImmutabilityTests
{
    private readonly LedgerDbFixture _fixture;

    public ImmutabilityTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task App_role_cannot_update_journal_lines()
    {
        var (space, _, lineId) = await SeedPostedEntryAsync();

        await using var connection = await _fixture.OpenAppAsync(space.SpaceId);
        await using var cmd = new NpgsqlCommand(
            "UPDATE journal_lines SET amount_minor = 0 WHERE id = @id;", connection);
        cmd.Parameters.AddWithValue("id", lineId);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState);
    }

    [Fact]
    public async Task App_role_cannot_delete_journal_entries()
    {
        var (space, entryId, _) = await SeedPostedEntryAsync();

        await using var connection = await _fixture.OpenAppAsync(space.SpaceId);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM journal_entries WHERE id = @id;", connection);
        cmd.Parameters.AddWithValue("id", entryId);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState);
    }

    [Fact]
    public async Task App_role_can_insert_a_reversal_entry_linked_via_reverses_entry_id()
    {
        var (space, originalEntryId, _) = await SeedPostedEntryAsync();
        var reversalId = Guid.NewGuid();

        // Corrections are new (reversal) entries, not mutations: an INSERT that references the
        // original via reverses_entry_id must succeed for the app role.
        await using var connection = await _fixture.OpenAppAsync(space.SpaceId);
        await using var tx = await connection.BeginTransactionAsync();

        await using (var entryCmd = new NpgsqlCommand(
            "INSERT INTO journal_entries " +
            "(id, space_id, entry_no, date, status, reverses_entry_id, created_by, created_at) " +
            "VALUES (@id, @sid, 2, current_date, 'posted', @orig, @by, now());",
            connection, tx))
        {
            entryCmd.Parameters.AddWithValue("id", reversalId);
            entryCmd.Parameters.AddWithValue("sid", space.SpaceId);
            entryCmd.Parameters.AddWithValue("orig", originalEntryId);
            entryCmd.Parameters.AddWithValue("by", Guid.NewGuid());
            await entryCmd.ExecuteNonQueryAsync();
        }

        // Balanced reversal lines so the deferred balance trigger passes at COMMIT.
        await InsertLineAsync(connection, tx, space, reversalId, Guid.NewGuid(), 1000);
        await InsertLineAsync(connection, tx, space, reversalId, Guid.NewGuid(), -1000);

        await tx.CommitAsync();

        await using var check = new NpgsqlCommand(
            "SELECT reverses_entry_id FROM journal_entries WHERE id = @id;", connection);
        check.Parameters.AddWithValue("id", reversalId);
        var linked = (Guid)(await check.ExecuteScalarAsync())!;
        Assert.Equal(originalEntryId, linked);
    }

    private async Task<(SeededSpace Space, Guid EntryId, Guid LineId)> SeedPostedEntryAsync()
    {
        var space = await _fixture.SeedSpaceAsync();
        var entryId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var tx = await connection.BeginTransactionAsync();

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

        await InsertLineAsync(connection, tx, space, entryId, lineId, 1000);
        await InsertLineAsync(connection, tx, space, entryId, Guid.NewGuid(), -1000);

        await tx.CommitAsync();
        return (space, entryId, lineId);
    }

    private static async Task InsertLineAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, SeededSpace space, Guid entryId, Guid lineId, long amount)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO journal_lines " +
            "(id, entry_id, space_id, account_id, amount_minor, currency, base_amount_minor) " +
            "VALUES (@id, @eid, @sid, @aid, @amt, 'CHF', @base);",
            connection, tx);
        cmd.Parameters.AddWithValue("id", lineId);
        cmd.Parameters.AddWithValue("eid", entryId);
        cmd.Parameters.AddWithValue("sid", space.SpaceId);
        cmd.Parameters.AddWithValue("aid", space.AccountId);
        cmd.Parameters.AddWithValue("amt", amount);
        cmd.Parameters.AddWithValue("base", amount);
        await cmd.ExecuteNonQueryAsync();
    }
}
