using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeafLedger.Modules.Ledger.Infrastructure;

internal sealed class SpaceMembershipQuery : ISpaceMembershipQuery
{
    private readonly LedgerDbContext _db;

    public SpaceMembershipQuery(LedgerDbContext db) => _db = db;

    public async Task<string?> GetRoleAsync(
        Guid spaceId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = (NpgsqlTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (var binding = new NpgsqlCommand(
            "SET LOCAL ROLE leafledger_app; " +
            "SELECT set_config('app.current_space_id', @space, true); " +
            "SELECT set_config('app.current_actor', @actor, true);",
            connection,
            transaction))
        {
            binding.Parameters.AddWithValue("space", spaceId.ToString());
            binding.Parameters.AddWithValue("actor", userId.ToString());
            await binding.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = new NpgsqlCommand(
            "SELECT role FROM memberships " +
            "WHERE space_id = @space AND user_id = @user " +
            "ORDER BY id LIMIT 1;",
            connection,
            transaction);
        command.Parameters.AddWithValue("space", spaceId);
        command.Parameters.AddWithValue("user", userId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : (string)value;
    }
}