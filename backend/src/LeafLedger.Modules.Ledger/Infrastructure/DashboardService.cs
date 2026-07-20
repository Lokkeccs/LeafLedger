using LeafLedger.Modules.Ledger.Application.Reporting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeafLedger.Modules.Ledger.Infrastructure;

internal sealed class DashboardService : IDashboardService
{
    private readonly LedgerDbContext _db;

    public DashboardService(LedgerDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardSummaryReport> GetDashboardSummaryAsync(
        Guid spaceId,
        CancellationToken cancellationToken = default)
    {
        await using var binding = await OpenBoundConnectionAsync(spaceId, cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT total_assets_minor, total_liabilities_minor, total_equity_minor, " +
            "total_income_minor, total_expenses_minor, account_count " +
            "FROM dashboard_summary;",
            binding.Connection,
            binding.Transaction);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Dashboard summary view returned no row.");
        }

        var totalAssetsMinor = reader.GetInt64(0);
        var totalLiabilitiesMinor = reader.GetInt64(1);
        var totalEquityMinor = reader.GetInt64(2);
        var totalIncomeMinor = reader.GetInt64(3);
        var totalExpensesMinor = reader.GetInt64(4);
        var accountCount = checked((int)reader.GetInt64(5));
        var netResultMinor = checked(totalIncomeMinor - totalExpensesMinor);
        var netWorthMinor = checked(totalAssetsMinor - totalLiabilitiesMinor);

        await reader.DisposeAsync().ConfigureAwait(false);
        await using var integrityCommand = new NpgsqlCommand(
            "SELECT COALESCE(SUM(base_balance_minor), 0)::bigint FROM trial_balance_live;",
            binding.Connection,
            binding.Transaction);
        var liveTotal = (long)(await integrityCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;

        return new DashboardSummaryReport(
            spaceId,
            totalAssetsMinor,
            totalLiabilitiesMinor,
            totalEquityMinor,
            totalIncomeMinor,
            totalExpensesMinor,
            netResultMinor,
            netWorthMinor,
            accountCount,
            liveTotal == 0);
    }

    private async Task<BoundDashboardConnection> OpenBoundConnectionAsync(
        Guid spaceId,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = new NpgsqlCommand(
                "SET LOCAL ROLE leafledger_app; SELECT set_config('app.current_space_id', @space, true);",
                connection,
                transaction);
            command.Parameters.AddWithValue("space", spaceId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new BoundDashboardConnection(connection, transaction);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class BoundDashboardConnection : IAsyncDisposable
    {
        public BoundDashboardConnection(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            Connection = connection;
            Transaction = transaction;
        }

        public NpgsqlConnection Connection { get; }

        public NpgsqlTransaction Transaction { get; }

        public async ValueTask DisposeAsync()
        {
            await Transaction.RollbackAsync().ConfigureAwait(false);
            await Transaction.DisposeAsync().ConfigureAwait(false);
        }
    }
}