using LeafLedger.Modules.Ledger.Application.Accounts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeafLedger.Modules.Ledger.Infrastructure;

internal sealed class AccountCatalogService : IAccountCatalogService
{
    private readonly LedgerDbContext _db;

    public AccountCatalogService(LedgerDbContext db) => _db = db;

    public async Task<AccountCatalogReport> GetAccountsAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        await using var binding = await OpenBoundConnectionAsync(spaceId, cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT id, code, name, currency, kind, is_active, group_id, valid_from, valid_to, fx_policy " +
            "FROM accounts ORDER BY code, id;",
            binding.Connection,
            binding.Transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var accounts = new List<AccountView>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            accounts.Add(new AccountView(
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3).Trim(),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateOnly>(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return new AccountCatalogReport(spaceId, accounts);
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