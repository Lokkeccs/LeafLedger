using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger.FinancialProperties;

[Trait("Category", "Property")]
[Collection("Ledger schema")]
public sealed class DatabaseSecondWallPropertyTests
{
    private readonly LedgerDbFixture _fixture;

    public DatabaseSecondWallPropertyTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Generated_unbalanced_line_sets_fail_at_deferred_commit()
    {
        var seed = 0xDB2AUL;
        Console.WriteLine($"WP08 I6 balance-trigger seed: {seed}");

        for (var iteration = 0; iteration < 6; iteration++)
        {
            var random = new Random(unchecked((int)(seed + (ulong)iteration)));
            var debit = random.Next(1, 1001);
            var credit = debit == 1 ? debit + 1 : debit - 1;
            var space = await _fixture.SeedSpaceAsync();
            await using var connection = await _fixture.OpenAppAsync(space.SpaceId);
            await using var transaction = await connection.BeginTransactionAsync();
            var entryId = Guid.NewGuid();

            await using (var entry = new NpgsqlCommand(
                "INSERT INTO journal_entries (id, space_id, entry_no, date, status, created_by, created_at) " +
                "VALUES (@id, @space, 1, DATE '2026-06-30', 'posted', @actor, now());",
                connection, transaction))
            {
                entry.Parameters.AddWithValue("id", entryId);
                entry.Parameters.AddWithValue("space", space.SpaceId);
                entry.Parameters.AddWithValue("actor", Guid.NewGuid());
                await entry.ExecuteNonQueryAsync();
            }

            await using (var lines = new NpgsqlCommand(
                "INSERT INTO journal_lines (id, entry_id, space_id, account_id, amount_minor, currency, base_amount_minor) " +
                "VALUES (@line1, @entry, @space, @account, @debit, 'CHF', @debit), " +
                "(@line2, @entry, @space, @account, @credit, 'CHF', @credit);",
                connection, transaction))
            {
                lines.Parameters.AddWithValue("line1", Guid.NewGuid());
                lines.Parameters.AddWithValue("line2", Guid.NewGuid());
                lines.Parameters.AddWithValue("entry", entryId);
                lines.Parameters.AddWithValue("space", space.SpaceId);
                lines.Parameters.AddWithValue("account", space.AccountId);
                lines.Parameters.AddWithValue("debit", debit);
                lines.Parameters.AddWithValue("credit", -credit);
                await lines.ExecuteNonQueryAsync();
            }

            var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
            Assert.Equal("23514", exception.SqlState);
        }
    }

    [Fact]
    public async Task Generated_space_pairs_are_isolated_by_rls()
    {
        var seed = 0x7A5AUL;
        Console.WriteLine($"WP08 I6 RLS seed: {seed}");

        for (var iteration = 0; iteration < 6; iteration++)
        {
            var random = new Random(unchecked((int)(seed + (ulong)iteration)));
            var first = await _fixture.SeedSpaceAsync(accountCode: 1000 + random.Next(0, 100));
            var second = await _fixture.SeedSpaceAsync(accountCode: 1100 + random.Next(0, 100));
            await using var connection = await _fixture.OpenAppAsync(first.SpaceId);

            await using var count = new NpgsqlCommand("SELECT count(*) FROM accounts;", connection);
            Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);

            await using var hidden = new NpgsqlCommand(
                "SELECT count(*) FROM accounts WHERE space_id = @space;", connection);
            hidden.Parameters.AddWithValue("space", second.SpaceId);
            Assert.Equal(0L, (long)(await hidden.ExecuteScalarAsync())!);

            await using var insert = new NpgsqlCommand(
                "INSERT INTO memberships (id, space_id, user_id, role, created_at) " +
                "VALUES (@id, @space, @user, 'viewer', now());", connection);
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("space", second.SpaceId);
            insert.Parameters.AddWithValue("user", Guid.NewGuid());
            var exception = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.Equal("42501", exception.SqlState);
        }

        await using var noContext = await _fixture.OpenAppNoContextAsync();
        await using var noContextCount = new NpgsqlCommand("SELECT count(*) FROM accounts;", noContext);
        Assert.Equal(0L, (long)(await noContextCount.ExecuteScalarAsync())!);
    }
}