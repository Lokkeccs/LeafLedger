using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class SchemaMigrationTests
{
    private readonly LedgerDbFixture _fixture;

    public SchemaMigrationTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Migration_leaves_no_pending_model_changes()
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        await using var context = new LedgerDbContext(options);

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("journal_lines", "amount_minor", "bigint")]
    [InlineData("journal_lines", "base_amount_minor", "bigint")]
    [InlineData("journal_entries", "entry_no", "bigint")]
    [InlineData("spaces", "id", "uuid")]
    [InlineData("journal_lines", "id", "uuid")]
    public async Task Money_and_id_columns_have_the_expected_storage_type(
        string table, string column, string expectedDataType)
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT data_type FROM information_schema.columns " +
            "WHERE table_name = @t AND column_name = @c;",
            connection);
        cmd.Parameters.AddWithValue("t", table);
        cmd.Parameters.AddWithValue("c", column);

        var dataType = (string?)await cmd.ExecuteScalarAsync();

        Assert.Equal(expectedDataType, dataType);
    }

    [Theory]
    [InlineData("spaces", "base_currency")]
    [InlineData("accounts", "currency")]
    [InlineData("journal_lines", "currency")]
    public async Task Currency_columns_are_fixed_char_3(string table, string column)
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT data_type, character_maximum_length FROM information_schema.columns " +
            "WHERE table_name = @t AND column_name = @c;",
            connection);
        cmd.Parameters.AddWithValue("t", table);
        cmd.Parameters.AddWithValue("c", column);

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("character", reader.GetString(0));
        Assert.Equal(3, reader.GetInt32(1));
    }

    [Fact]
    public async Task No_amount_column_uses_a_float_or_money_type()
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND left(table_name, 2) <> '__' " +
            "AND data_type IN ('real', 'double precision', 'money');",
            connection);
        var offenders = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(0, offenders);
    }

    [Fact]
    public async Task Fx_rate_is_the_only_numeric_column()
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT table_name || '.' || column_name FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND left(table_name, 2) <> '__' " +
            "AND data_type = 'numeric' ORDER BY 1;",
            connection);
        List<string> numericColumns = [];
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                numericColumns.Add(reader.GetString(0));
            }
        }

        var only = Assert.Single(numericColumns);
        Assert.Equal("journal_lines.fx_rate", only);
    }

    [Fact]
    public async Task Every_id_column_is_uuid()
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT table_name || '.' || column_name, data_type FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND left(table_name, 2) <> '__' " +
            "AND (column_name = 'id' OR column_name LIKE '%\\_id') AND data_type <> 'uuid';",
            connection);
        List<string> nonUuid = [];
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                nonUuid.Add($"{reader.GetString(0)} ({reader.GetString(1)})");
            }
        }

        Assert.Empty(nonUuid);
    }

    [Theory]
    [InlineData("spaces")]
    [InlineData("memberships")]
    [InlineData("account_groups")]
    [InlineData("accounts")]
    [InlineData("periods")]
    [InlineData("journal_entries")]
    [InlineData("journal_lines")]
    [InlineData("line_attributions")]
    [InlineData("audit_log")]
    public async Task Expected_table_exists(string table)
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_name = @t;",
            connection);
        cmd.Parameters.AddWithValue("t", table);
        var exists = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(1, exists);
    }
}
