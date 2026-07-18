using LeafLedger.Modules.Ledger.Application.Reporting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LeafLedger.Modules.Ledger.Infrastructure;

internal sealed class AccountLedgerService : IAccountLedgerService
{
    private readonly LedgerDbContext _db;

    public AccountLedgerService(LedgerDbContext db) => _db = db;

    public async Task<AccountLedgerReport> GetAccountLedgerAsync(
        Guid spaceId,
        Guid accountId,
        DateOnly? from,
        DateOnly? through,
        CancellationToken cancellationToken = default)
    {
        await using var binding = await OpenBoundConnectionAsync(spaceId, cancellationToken).ConfigureAwait(false);
        var account = await ReadAccountAsync(binding, accountId, cancellationToken).ConfigureAwait(false);
        var opening = from is null
            ? 0L
            : await ReadOpeningBalanceAsync(binding, accountId, from.Value, cancellationToken).ConfigureAwait(false);
        var lines = await ReadLinesAsync(binding, accountId, from, through, opening, cancellationToken).ConfigureAwait(false);
        var closing = checked(opening + lines.Sum(line => line.BaseAmountMinor));

        return new AccountLedgerReport(
            spaceId,
            accountId,
            account?.Code ?? 0,
            account?.Name ?? string.Empty,
            account?.Kind ?? string.Empty,
            account?.Currency ?? string.Empty,
            opening,
            closing,
            lines);
    }

    private static async Task<AccountHeader?> ReadAccountAsync(
        BoundConnection binding,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT code, name, kind, currency FROM accounts WHERE id = @account;",
            binding.Connection,
            binding.Transaction);
        command.Parameters.AddWithValue("account", accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new AccountHeader(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3).Trim());
    }

    private static async Task<long> ReadOpeningBalanceAsync(
        BoundConnection binding,
        Guid accountId,
        DateOnly from,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT COALESCE(SUM(jl.base_amount_minor), 0)::bigint " +
            "FROM journal_lines jl " +
            "JOIN journal_entries je ON je.id = jl.entry_id " +
            "WHERE jl.account_id = @account AND je.status = 'posted' AND je.date < @from;",
            binding.Connection,
            binding.Transaction);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is long balance ? balance : 0L;
    }

    private static async Task<IReadOnlyList<AccountLedgerLine>> ReadLinesAsync(
        BoundConnection binding,
        Guid accountId,
        DateOnly? from,
        DateOnly? to,
        long opening,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT je.id, je.entry_no, je.date, je.description, je.reference, " +
            "jl.amount_minor, jl.base_amount_minor, jl.currency, " +
            "(@opening + SUM(jl.base_amount_minor) OVER (ORDER BY je.date, je.entry_no, jl.id))::bigint " +
            "FROM journal_lines jl " +
            "JOIN journal_entries je ON je.id = jl.entry_id " +
            "WHERE jl.account_id = @account AND je.status = 'posted' " +
            "AND (@from IS NULL OR je.date >= @from) " +
            "AND (@to IS NULL OR je.date <= @to) " +
            "ORDER BY je.date, je.entry_no, jl.id;",
            binding.Connection,
            binding.Transaction);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("opening", opening);
        command.Parameters.AddWithValue("from", NpgsqlDbType.Date, from.HasValue ? from.Value : DBNull.Value);
        command.Parameters.AddWithValue("to", NpgsqlDbType.Date, to.HasValue ? to.Value : DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var lines = new List<AccountLedgerLine>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            lines.Add(new AccountLedgerLine(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetString(7).Trim(),
                reader.GetInt64(8)));
        }

        return lines;
    }

    private async Task<BoundConnection> OpenBoundConnectionAsync(Guid spaceId, CancellationToken cancellationToken)
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
            return new BoundConnection(connection, transaction);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed record AccountHeader(int Code, string Name, string Kind, string Currency);

    private sealed class BoundConnection(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public NpgsqlConnection Connection { get; } = connection;

        public NpgsqlTransaction Transaction { get; } = transaction;

        public async ValueTask DisposeAsync()
        {
            await Transaction.RollbackAsync().ConfigureAwait(false);
            await Transaction.DisposeAsync().ConfigureAwait(false);
        }
    }
}