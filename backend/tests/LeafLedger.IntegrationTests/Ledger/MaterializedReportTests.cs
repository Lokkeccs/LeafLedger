using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class MaterializedReportTests
{
    private readonly LedgerDbFixture _fixture;

    public MaterializedReportTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Wrapper_is_fail_closed_and_matview_is_not_selectable_by_app_role()
    {
        var space = await _fixture.SeedSpaceAsync();
        await using var connection = await _fixture.OpenAppAsync(space.SpaceId);

        await using var wrapper = new NpgsqlCommand("SELECT count(*) FROM trial_balance;", connection);
        Assert.Equal(0L, Convert.ToInt64(await wrapper.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));

        await using var direct = new NpgsqlCommand("SELECT count(*) FROM trial_balance_mat;", connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => direct.ExecuteScalarAsync());
        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    public async Task No_context_wrapper_returns_zero_and_refresh_function_is_granted()
    {
        await using var connection = await _fixture.OpenAppNoContextAsync();

        await using var wrapper = new NpgsqlCommand("SELECT count(*) FROM trial_balance;", connection);
        Assert.Equal(0L, Convert.ToInt64(await wrapper.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));

        await using var refresh = new NpgsqlCommand("SELECT refresh_trial_balance_mat();", connection);
        Assert.True(Convert.ToInt64(await refresh.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) >= 0);
    }

    [Fact]
    public async Task Populated_wrapper_isolation_excludes_rows_from_other_spaces()
    {
        var first = await _fixture.SeedSpaceAsync();
        var second = await _fixture.SeedSpaceAsync();
        var firstOtherAccount = await SeedBalancedEntryAsync(first, 100);
        var secondOtherAccount = await SeedBalancedEntryAsync(second, 200);

        await using var superuser = await _fixture.OpenSuperuserAsync();
        await using (var refresh = new NpgsqlCommand("SELECT refresh_trial_balance_mat();", superuser))
        {
            await refresh.ExecuteScalarAsync();
        }

        await using var app = await _fixture.OpenAppAsync(first.SpaceId);
        await using var rows = new NpgsqlCommand(
            "SELECT account_id FROM trial_balance ORDER BY account_id;",
            app);
        await using var reader = await rows.ExecuteReaderAsync();
        var visibleAccountIds = new List<Guid>();
        while (await reader.ReadAsync())
        {
            visibleAccountIds.Add(reader.GetGuid(0));
        }

        Assert.Contains(first.AccountId, visibleAccountIds);
        Assert.Contains(firstOtherAccount, visibleAccountIds);
        Assert.DoesNotContain(second.AccountId, visibleAccountIds);
        Assert.DoesNotContain(secondOtherAccount, visibleAccountIds);
    }

    private async Task<Guid> SeedBalancedEntryAsync(SeededSpace space, long amount)
    {
        var otherAccountId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO accounts (id, space_id, group_id, code, name, currency, kind, is_active, created_at) " +
            "VALUES (@account, @space, @group, @code, 'Other account', 'CHF', 'asset', true, now()); " +
            "INSERT INTO journal_entries (id, space_id, entry_no, date, status, description, created_by, created_at) " +
            "VALUES (@entry, @space, 1, DATE '2026-06-30', 'posted', 'Tenancy fixture', @actor, now()); " +
            "INSERT INTO journal_lines (id, entry_id, space_id, account_id, amount_minor, currency, base_amount_minor) VALUES " +
            "(@line1, @entry, @space, @account, @amount, 'CHF', @amount), " +
            "(@line2, @entry, @space, @spaceAccount, -@amount, 'CHF', -@amount);",
            connection);
        command.Parameters.AddWithValue("account", otherAccountId);
        command.Parameters.AddWithValue("space", space.SpaceId);
        command.Parameters.AddWithValue("group", space.GroupId);
        command.Parameters.AddWithValue("code", amount == 100 ? 1001 : 1002);
        command.Parameters.AddWithValue("entry", entryId);
        command.Parameters.AddWithValue("actor", Guid.NewGuid());
        command.Parameters.AddWithValue("line1", Guid.NewGuid());
        command.Parameters.AddWithValue("line2", Guid.NewGuid());
        command.Parameters.AddWithValue("spaceAccount", space.AccountId);
        command.Parameters.AddWithValue("amount", amount);
        await command.ExecuteNonQueryAsync();
        return otherAccountId;
    }
}