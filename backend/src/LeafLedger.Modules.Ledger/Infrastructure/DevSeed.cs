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

        await EnsureSpaceAsync(connection, spaceId, devUserId, cancellationToken).ConfigureAwait(false);
        await EnsureRealUserMembershipAsync(connection, configuration, spaceId, cancellationToken).ConfigureAwait(false);
        await EnsureE2EMemberMembershipAsync(
            connection,
            configuration,
            spaceId,
            "Seed:E2EMemberASubject",
            "Seed:E2EMemberATenant",
            cancellationToken).ConfigureAwait(false);
        await EnsureE2EMemberMembershipAsync(
            connection,
            configuration,
            spaceId,
            "Seed:E2EMemberBSubject",
            "Seed:E2EMemberBTenant",
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSpaceAsync(
        NpgsqlConnection connection,
        Guid spaceId,
        Guid devUserId,
        CancellationToken cancellationToken)
    {
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

    // Grants the interactively signed-in developer account access to the demo space.
    // The real Entra identity (subject/oid + tenant) resolves to an internal user id via
    // resolve_identity_link; that id is what memberships are keyed by. Without this, a real
    // sign-in resolves to a fresh internal user with no membership and every request 403s.
    private static async Task EnsureRealUserMembershipAsync(
        NpgsqlConnection connection,
        IConfiguration configuration,
        Guid spaceId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(configuration["Seed:DevUserSubject"], out var subject) ||
            !Guid.TryParse(configuration["Seed:DevUserTenant"], out var tenantId))
        {
            return;
        }

        Guid internalUserId;
        await using (var linkTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            await using var resolve = new NpgsqlCommand(
                "SET LOCAL ROLE leafledger_app; SELECT resolve_identity_link(@subject, @tenant);",
                connection,
                linkTransaction);
            resolve.Parameters.AddWithValue("subject", subject);
            resolve.Parameters.AddWithValue("tenant", tenantId);
            var resolved = await resolve.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (resolved is not Guid userId)
            {
                await linkTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            internalUserId = userId;
            await linkTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var membershipTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var membership = new NpgsqlCommand(
            "INSERT INTO memberships (id, space_id, user_id, role, created_at) " +
            "SELECT @membership, @space, @user, 'Owner', now() " +
            "WHERE NOT EXISTS (SELECT 1 FROM memberships WHERE space_id = @space AND user_id = @user);",
            connection,
            membershipTransaction);
        membership.Parameters.AddWithValue("membership", Guid.NewGuid());
        membership.Parameters.AddWithValue("space", spaceId);
        membership.Parameters.AddWithValue("user", internalUserId);
        await membership.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await membershipTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureE2EMemberMembershipAsync(
        NpgsqlConnection connection,
        IConfiguration configuration,
        Guid spaceId,
        string subjectKey,
        string tenantKey,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(configuration[subjectKey], out var subject) ||
            !Guid.TryParse(configuration[tenantKey], out var tenantId))
        {
            return;
        }

        Guid internalUserId;
        await using (var linkTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            await using var resolve = new NpgsqlCommand(
                "SET LOCAL ROLE leafledger_app; SELECT resolve_identity_link(@subject, @tenant);",
                connection,
                linkTransaction);
            resolve.Parameters.AddWithValue("subject", subject);
            resolve.Parameters.AddWithValue("tenant", tenantId);
            var resolved = await resolve.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (resolved is not Guid userId)
            {
                await linkTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            internalUserId = userId;
            await linkTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var membershipTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var membership = new NpgsqlCommand(
            "INSERT INTO memberships (id, space_id, user_id, role, created_at) " +
            "SELECT @membership, @space, @user, 'Owner', now() " +
            "WHERE NOT EXISTS (SELECT 1 FROM memberships WHERE space_id = @space AND user_id = @user);",
            connection,
            membershipTransaction);
        membership.Parameters.AddWithValue("membership", Guid.NewGuid());
        membership.Parameters.AddWithValue("space", spaceId);
        membership.Parameters.AddWithValue("user", internalUserId);
        await membership.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await membershipTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}