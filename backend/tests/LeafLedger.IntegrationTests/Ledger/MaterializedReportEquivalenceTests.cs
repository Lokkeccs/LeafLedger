using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class MaterializedReportEquivalenceTests
{
    private readonly LedgerDbFixture _fixture;

    public MaterializedReportEquivalenceTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Materialized_and_live_reporting_views_are_equivalent_after_refresh()
    {
        var space = await _fixture.SeedSpaceAsync();
        var accounts = await SeedLedgerAsync(space.SpaceId, space.GroupId);

        await using var liveConnection = await _fixture.OpenSuperuserAsync();
        var liveTrial = await ReadRowsAsync(liveConnection,
            "SELECT account_id, account_kind, base_balance_minor FROM trial_balance_live WHERE space_id = @space ORDER BY account_id;",
            space.SpaceId);

        await using (var refresh = new NpgsqlCommand("SELECT refresh_trial_balance_mat();", liveConnection))
        {
            await refresh.ExecuteScalarAsync();
        }

        await using var appConnection = await _fixture.OpenAppAsync(space.SpaceId);
        var materializedTrial = await ReadRowsAsync(appConnection,
            "SELECT account_id, account_kind, base_balance_minor FROM trial_balance ORDER BY account_id;",
            null);
        Assert.Equal(liveTrial, materializedTrial);
        Assert.Equal(0L, materializedTrial.Sum(row => row.Balance));

        var balanceSheet = await ReadRowsAsync(appConnection,
            "SELECT account_id, account_kind, amount_minor FROM balance_sheet_lines ORDER BY account_id;",
            null);
        Assert.Equal(
            liveTrial.Where(row => row.Kind is "asset" or "liability" or "equity")
                .Select(row => new ReportRow(row.AccountId, row.Kind, row.Kind == "asset" ? row.Balance : -row.Balance)),
            balanceSheet);

        var incomeStatement = await ReadRowsAsync(appConnection,
            "SELECT account_id, account_kind, amount_minor FROM income_statement_lines ORDER BY account_id;",
            null);
        Assert.Equal(
            liveTrial.Where(row => row.Kind is "income" or "expense")
                .Select(row => new ReportRow(row.AccountId, row.Kind, row.Kind == "income" ? -row.Balance : row.Balance)),
            incomeStatement);
        Assert.Equal(accounts.IncomeId, incomeStatement.Single(row => row.Kind == "income").AccountId);
    }

    private async Task<SeededAccounts> SeedLedgerAsync(Guid spaceId, Guid groupId)
    {
        var assetId = Guid.NewGuid();
        var incomeId = Guid.NewGuid();
        var sourceEntryId = Guid.NewGuid();
        var reversalEntryId = Guid.NewGuid();
        var liveEntryId = Guid.NewGuid();

        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO accounts (id, space_id, group_id, code, name, currency, kind, is_active, created_at) VALUES " +
            "(@asset, @space, @group, 3000, 'Materialized asset', 'CHF', 'asset', true, now()), " +
            "(@income, @space, @group, 3001, 'Materialized income', 'CHF', 'income', true, now()); " +
            "INSERT INTO journal_entries (id, space_id, entry_no, date, status, description, created_by, created_at, reverses_entry_id) VALUES " +
            "(@source, @space, 1, DATE '2026-06-30', 'posted', 'Source', @actor, now(), NULL), " +
            "(@reversal, @space, 2, DATE '2026-06-30', 'posted', 'Reversal', @actor, now(), @source), " +
            "(@live, @space, 3, DATE '2026-06-30', 'posted', 'Live entry', @actor, now(), NULL); " +
            "INSERT INTO journal_lines (id, entry_id, space_id, account_id, amount_minor, currency, base_amount_minor) VALUES " +
            "(@sourceAssetLine, @source, @space, @asset, 100, 'CHF', 100), (@sourceIncomeLine, @source, @space, @income, -100, 'CHF', -100), " +
            "(@reversalAssetLine, @reversal, @space, @asset, -100, 'CHF', -100), (@reversalIncomeLine, @reversal, @space, @income, 100, 'CHF', 100), " +
            "(@liveAssetLine, @live, @space, @asset, 70, 'CHF', 70), (@liveIncomeLine, @live, @space, @income, -70, 'CHF', -70);",
            connection);
        command.Parameters.AddWithValue("asset", assetId);
        command.Parameters.AddWithValue("income", incomeId);
        command.Parameters.AddWithValue("space", spaceId);
        command.Parameters.AddWithValue("group", groupId);
        command.Parameters.AddWithValue("source", sourceEntryId);
        command.Parameters.AddWithValue("reversal", reversalEntryId);
        command.Parameters.AddWithValue("live", liveEntryId);
        command.Parameters.AddWithValue("actor", Guid.NewGuid());
        command.Parameters.AddWithValue("sourceAssetLine", Guid.NewGuid());
        command.Parameters.AddWithValue("sourceIncomeLine", Guid.NewGuid());
        command.Parameters.AddWithValue("reversalAssetLine", Guid.NewGuid());
        command.Parameters.AddWithValue("reversalIncomeLine", Guid.NewGuid());
        command.Parameters.AddWithValue("liveAssetLine", Guid.NewGuid());
        command.Parameters.AddWithValue("liveIncomeLine", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
        return new SeededAccounts(incomeId);
    }

    private static async Task<IReadOnlyList<ReportRow>> ReadRowsAsync(NpgsqlConnection connection, string sql, Guid? spaceId)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        if (spaceId is not null)
        {
            command.Parameters.AddWithValue("space", spaceId.Value);
        }

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<ReportRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new ReportRow(reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2)));
        }

        return rows;
    }

    private sealed record SeededAccounts(Guid IncomeId);

    private sealed record ReportRow(Guid AccountId, string Kind, long Balance);
}