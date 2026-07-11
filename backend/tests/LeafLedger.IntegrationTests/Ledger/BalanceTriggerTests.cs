using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class BalanceTriggerTests
{
    private readonly LedgerDbFixture _fixture;

    public BalanceTriggerTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Balanced_entry_commits()
    {
        var space = await _fixture.SeedSpaceAsync();
        await using var connection = await _fixture.OpenAppAsync(space.SpaceId);

        await using var tx = await connection.BeginTransactionAsync();
        await InsertEntryWithLinesAsync(connection, tx, space, entryNo: 1, baseAmounts: [1000, -1000]);

        // Deferred constraint validates at COMMIT; a balanced entry succeeds.
        await tx.CommitAsync();
    }

    [Fact]
    public async Task Unbalanced_entry_is_rejected_at_commit()
    {
        var space = await _fixture.SeedSpaceAsync();
        await using var connection = await _fixture.OpenAppAsync(space.SpaceId);

        await using var tx = await connection.BeginTransactionAsync();
        await InsertEntryWithLinesAsync(connection, tx, space, entryNo: 2, baseAmounts: [1000, -500]);

        // The deferred balance trigger fires at COMMIT and raises a check violation.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
        Assert.Equal("23514", ex.SqlState);
    }

    private static async Task InsertEntryWithLinesAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, SeededSpace space, long entryNo, long[] baseAmounts)
    {
        var entryId = Guid.NewGuid();
        await using (var entryCmd = new NpgsqlCommand(
            "INSERT INTO journal_entries (id, space_id, entry_no, date, status, created_by, created_at) " +
            "VALUES (@id, @sid, @no, current_date, 'posted', @by, now());",
            connection, tx))
        {
            entryCmd.Parameters.AddWithValue("id", entryId);
            entryCmd.Parameters.AddWithValue("sid", space.SpaceId);
            entryCmd.Parameters.AddWithValue("no", entryNo);
            entryCmd.Parameters.AddWithValue("by", Guid.NewGuid());
            await entryCmd.ExecuteNonQueryAsync();
        }

        foreach (var amount in baseAmounts)
        {
            await using var lineCmd = new NpgsqlCommand(
                "INSERT INTO journal_lines " +
                "(id, entry_id, space_id, account_id, amount_minor, currency, base_amount_minor) " +
                "VALUES (@id, @eid, @sid, @aid, @amt, 'CHF', @base);",
                connection, tx);
            lineCmd.Parameters.AddWithValue("id", Guid.NewGuid());
            lineCmd.Parameters.AddWithValue("eid", entryId);
            lineCmd.Parameters.AddWithValue("sid", space.SpaceId);
            lineCmd.Parameters.AddWithValue("aid", space.AccountId);
            lineCmd.Parameters.AddWithValue("amt", amount);
            lineCmd.Parameters.AddWithValue("base", amount);
            await lineCmd.ExecuteNonQueryAsync();
        }
    }
}
