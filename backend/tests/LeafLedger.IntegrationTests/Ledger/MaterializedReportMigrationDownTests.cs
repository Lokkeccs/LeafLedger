using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
public sealed class MaterializedReportMigrationDownTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260712150000_PeriodOverlapConstraint";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("leafledger_down_test")
        .WithUsername("testsuper")
        .WithPassword("testpassword")
        .Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Down_restores_plain_reporting_views_and_removes_materialized_objects()
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        await using (var context = new LedgerDbContext(options))
        {
            await context.Database.MigrateAsync();
            await using var migratedConnection = new NpgsqlConnection(_container.GetConnectionString());
            await migratedConnection.OpenAsync();
            Assert.True(await ExistsAsync(
                migratedConnection,
                "SELECT EXISTS (SELECT 1 FROM pg_class WHERE relname = 'dashboard_summary' AND relkind = 'v');"));
            await context.Database.MigrateAsync(PreviousMigration);
        }

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        Assert.Equal('v', await ScalarAsync(connection, "SELECT relkind FROM pg_class WHERE oid = 'trial_balance'::regclass;"));
        Assert.Equal('v', await ScalarAsync(connection, "SELECT relkind FROM pg_class WHERE oid = 'balance_sheet_lines'::regclass;"));
        Assert.Equal('v', await ScalarAsync(connection, "SELECT relkind FROM pg_class WHERE oid = 'income_statement_lines'::regclass;"));
        Assert.False(await ExistsAsync(connection, "SELECT EXISTS (SELECT 1 FROM pg_class WHERE relname = 'trial_balance_mat');"));
        Assert.False(await ExistsAsync(connection, "SELECT EXISTS (SELECT 1 FROM pg_class WHERE relname = 'ux_trial_balance_mat');"));
        Assert.False(await ExistsAsync(connection, "SELECT EXISTS (SELECT 1 FROM pg_proc WHERE proname = 'refresh_trial_balance_mat');"));
        Assert.False(await ExistsAsync(connection, "SELECT EXISTS (SELECT 1 FROM pg_class WHERE relname = 'dashboard_summary');"));

        var viewDefinition = (string?)await ScalarAsync(
            connection,
            "SELECT pg_get_viewdef('trial_balance'::regclass, true);");
        Assert.NotNull(viewDefinition);
        Assert.DoesNotContain("trial_balance_mat", viewDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("journal_lines", viewDefinition, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(3L, (long)(await ScalarAsync(
            connection,
            "SELECT count(*) FROM information_schema.views WHERE table_name IN ('trial_balance', 'balance_sheet_lines', 'income_statement_lines');"))!);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, string sql)
    {
        return (bool)(await ScalarAsync(connection, sql))!;
    }
}