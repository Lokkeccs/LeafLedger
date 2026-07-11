using LeafLedger.Modules.Ledger.Application.Reporting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeafLedger.Modules.Ledger.Infrastructure;

internal sealed class LedgerReportService : ILedgerReportService
{
    private readonly LedgerDbContext _db;

    public LedgerReportService(LedgerDbContext db) => _db = db;

    public async Task<TrialBalanceReport> GetTrialBalanceAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        var rows = await ReadTrialBalanceAsync(spaceId, cancellationToken).ConfigureAwait(false);
        return new TrialBalanceReport(spaceId, rows, rows.Sum(row => row.BaseBalanceMinor));
    }

    public async Task<BalanceSheetReport> GetBalanceSheetAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        var rows = await ReadViewRowsAsync(spaceId, "balance_sheet_lines", cancellationToken).ConfigureAwait(false);
        var lines = rows.Select(row => new ReportLine(
            row.AccountId,
            row.AccountCode,
            row.AccountName,
            row.AccountKind,
            row.BaseBalanceMinor,
            false)).ToArray();
        var incomeRows = await ReadViewRowsAsync(spaceId, "income_statement_lines", cancellationToken).ConfigureAwait(false);
        var currentResult = CalculateNetResult(incomeRows);
        return new BalanceSheetReport(spaceId,
            lines.Append(new ReportLine(null, null, "Current result", "equity", currentResult, true)).ToArray(),
            currentResult);
    }

    public async Task<IncomeStatementReport> GetIncomeStatementAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        var rows = await ReadViewRowsAsync(spaceId, "income_statement_lines", cancellationToken).ConfigureAwait(false);
        var lines = rows.Select(row => new ReportLine(
            row.AccountId,
            row.AccountCode,
            row.AccountName,
            row.AccountKind,
            row.BaseBalanceMinor,
            false)).ToList();
        var netResult = CalculateNetResult(rows);
        lines.Add(new ReportLine(null, null, "Net result", "NetResult", netResult, true));
        return new IncomeStatementReport(spaceId, lines, netResult);
    }

    public async Task<IntegrityReport> GetIntegrityAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        var rows = await ReadTrialBalanceAsync(spaceId, cancellationToken).ConfigureAwait(false);
        var hashRows = rows.Select(row => new IntegrityBalanceRow(row.AccountId, row.AccountCode, row.BaseBalanceMinor));
        var total = rows.Sum(row => row.BaseBalanceMinor);
        return new IntegrityReport(spaceId, IntegrityHasher.Algorithm, IntegrityHasher.Version, rows.Count,
            IntegrityHasher.Compute(spaceId, hashRows), total == 0);
    }

    private async Task<IReadOnlyList<TrialBalanceRow>> ReadTrialBalanceAsync(Guid spaceId, CancellationToken cancellationToken)
    {
        await using var binding = await OpenBoundConnectionAsync(spaceId, cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT account_id, account_code, account_name, account_kind, base_balance_minor FROM trial_balance ORDER BY account_code, account_id;",
            binding.Connection,
            binding.Transaction);
        return await ReadRowsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TrialBalanceRow>> ReadViewRowsAsync(Guid spaceId, string viewName, CancellationToken cancellationToken)
    {
        if (viewName is not ("balance_sheet_lines" or "income_statement_lines"))
        {
            throw new ArgumentException("Unknown report view.", nameof(viewName));
        }

        await using var binding = await OpenBoundConnectionAsync(spaceId, cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            $"SELECT account_id, account_code, account_name, account_kind, amount_minor FROM {viewName} ORDER BY account_code, account_id;",
            binding.Connection,
            binding.Transaction);
        return await ReadRowsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<TrialBalanceRow>> ReadRowsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<TrialBalanceRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new TrialBalanceRow(
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4)));
        }

        return rows;
    }

    private static long CalculateNetResult(IEnumerable<TrialBalanceRow> rows)
    {
        var income = rows.Where(row => row.AccountKind.Equals("Income", StringComparison.OrdinalIgnoreCase))
            .Sum(row => row.BaseBalanceMinor);
        var expenses = rows.Where(row => row.AccountKind.Equals("Expense", StringComparison.OrdinalIgnoreCase))
            .Sum(row => row.BaseBalanceMinor);
        return checked(income - expenses);
    }

    private async Task<BoundReportConnection> OpenBoundConnectionAsync(Guid spaceId, CancellationToken cancellationToken)
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
            return new BoundReportConnection(connection, transaction);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class BoundReportConnection : IAsyncDisposable
    {
        public BoundReportConnection(NpgsqlConnection connection, NpgsqlTransaction transaction)
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