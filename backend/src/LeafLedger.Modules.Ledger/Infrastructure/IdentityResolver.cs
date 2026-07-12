using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeafLedger.Modules.Ledger.Infrastructure;

internal sealed class IdentityResolver : IIdentityResolver
{
    private readonly LedgerDbContext _db;

    public IdentityResolver(LedgerDbContext db) => _db = db;

    public async Task<Guid> ResolveUserIdAsync(Guid subject, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = (NpgsqlTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SET LOCAL ROLE leafledger_app; SELECT resolve_identity_link(@subject, @tenant);",
            connection,
            transaction);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("tenant", tenantId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return value is Guid userId
            ? userId
            : throw new InvalidOperationException("The identity resolver returned no internal user id.");
    }
}