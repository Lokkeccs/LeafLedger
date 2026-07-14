using Microsoft.Extensions.Configuration;
using Npgsql;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public static class DevSeed
{
    private static readonly Guid DefaultSpaceId = Guid.Parse("8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8f8a1");

    public static async Task SeedAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!bool.TryParse(configuration["Seed:Enabled"], out var enabled) || !enabled)
        {
            return;
        }

        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString) || !Guid.TryParse(configuration["Seed:DevUserId"], out var devUserId))
        {
            return;
        }

        var spaceId = Guid.TryParse(configuration["Seed:SpaceId"], out var configuredSpaceId)
            ? configuredSpaceId
            : DefaultSpaceId;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var exists = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM spaces WHERE id = @space);", connection, transaction))
        {
            exists.Parameters.AddWithValue("space", spaceId);
            if ((bool)(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var assetGroupId = Guid.NewGuid();
        var expenseGroupId = Guid.NewGuid();
        await using var command = new NpgsqlCommand(
            "INSERT INTO spaces (id, name, base_currency, created_at) VALUES (@space, 'LeafLedger Demo', 'CHF', now()); " +
            "INSERT INTO account_groups (id, space_id, code_range, name, created_at) VALUES " +
            "(@assets, @space, int4range(1000, 2000), 'Assets', now()), " +
            "(@expenses, @space, int4range(6000, 7000), 'Expenses', now()); " +
            "INSERT INTO accounts (id, space_id, group_id, code, name, currency, kind, is_active, created_at) VALUES " +
            "(@cash, @space, @assets, 1000, 'Cash', 'CHF', 'asset', true, now()), " +
            "(@bank, @space, @assets, 1020, 'Bank', 'CHF', 'asset', true, now()), " +
            "(@expense, @space, @expenses, 6000, 'Office expenses', 'CHF', 'expense', true, now()); " +
            "INSERT INTO periods (id, space_id, name, start_date, end_exclusive, state, created_at) " +
            "VALUES (@period, @space, @period_name, @start, @end, 'open', now()); " +
            "INSERT INTO memberships (id, space_id, user_id, role, created_at) " +
            "VALUES (@membership, @space, @user, 'Owner', now());",
            connection,
            transaction);
        command.Parameters.AddWithValue("space", spaceId);
        command.Parameters.AddWithValue("assets", assetGroupId);
        command.Parameters.AddWithValue("expenses", expenseGroupId);
        command.Parameters.AddWithValue("cash", Guid.NewGuid());
        command.Parameters.AddWithValue("bank", Guid.NewGuid());
        command.Parameters.AddWithValue("expense", Guid.NewGuid());
        command.Parameters.AddWithValue("period", Guid.NewGuid());
        command.Parameters.AddWithValue("period_name", $"FY {DateTime.UtcNow.Year}");
        command.Parameters.AddWithValue("start", new DateOnly(DateTime.UtcNow.Year, 1, 1));
        command.Parameters.AddWithValue("end", new DateOnly(DateTime.UtcNow.Year + 1, 1, 1));
        command.Parameters.AddWithValue("membership", Guid.NewGuid());
        command.Parameters.AddWithValue("user", devUserId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}