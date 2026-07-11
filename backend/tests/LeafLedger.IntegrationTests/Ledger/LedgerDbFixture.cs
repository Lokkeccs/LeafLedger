using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

/// <summary>
/// Shared Postgres container with the Ledger schema migrated in. Tests connect either as the
/// container superuser (bypasses RLS, used for seeding) or as the least-privilege
/// <c>leafledger_app</c> role with the tenancy GUCs bound (the code path the app uses).
/// </summary>
public sealed class LedgerDbFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("leafledger_test")
        .WithUsername("testsuper")
        .WithPassword("testpassword")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        await using var context = new LedgerDbContext(options);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Opens a superuser connection (RLS-exempt) for seeding.</summary>
    public async Task<NpgsqlConnection> OpenSuperuserAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Opens a connection acting as <c>leafledger_app</c> with the tenancy GUCs set,
    /// i.e. the exact security context the application runs under.</summary>
    public async Task<NpgsqlConnection> OpenAppAsync(Guid spaceId, string actor = "integration-test")
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SET ROLE leafledger_app; " +
            "SELECT set_config('app.current_space_id', @space, false); " +
            "SELECT set_config('app.current_actor', @actor, false);",
            connection);
        cmd.Parameters.AddWithValue("space", spaceId.ToString());
        cmd.Parameters.AddWithValue("actor", actor);
        await cmd.ExecuteNonQueryAsync();
        return connection;
    }

    /// <summary>Opens a connection acting as <c>leafledger_app</c> with <b>no</b> tenancy GUCs
    /// bound — the fail-closed default: with no space context the policies resolve to
    /// <c>space_id = NULL</c>, so reads return 0 rows and writes are rejected. Uses a
    /// dedicated non-pooled connection so a leftover <c>app.current_space_id</c> from a
    /// pooled connection (Npgsql resets <c>SET ROLE</c> but not a <c>set_config()</c> GUC)
    /// cannot mask the fail-closed behaviour.</summary>
    public async Task<NpgsqlConnection> OpenAppNoContextAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString) { Pooling = false };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand("SET ROLE leafledger_app;", connection);
        await cmd.ExecuteNonQueryAsync();
        return connection;
    }

    /// <summary>Seeds a space with one account group and one account, as superuser.</summary>
    public async Task<SeededSpace> SeedSpaceAsync(int codeLow = 1000, int codeHigh = 2000, int accountCode = 1000)
    {
        var spaceId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await using var connection = await OpenSuperuserAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO spaces (id, name, base_currency, created_at) " +
            "VALUES (@sid, 'Test Space', 'CHF', now()); " +
            "INSERT INTO account_groups (id, space_id, code_range, name, created_at) " +
            "VALUES (@gid, @sid, int4range(@lo, @hi), 'Assets', now()); " +
            "INSERT INTO accounts (id, space_id, group_id, code, name, currency, kind, is_active, created_at) " +
            "VALUES (@aid, @sid, @gid, @code, 'Cash', 'CHF', 'asset', true, now());",
            connection);
        cmd.Parameters.AddWithValue("sid", spaceId);
        cmd.Parameters.AddWithValue("gid", groupId);
        cmd.Parameters.AddWithValue("aid", accountId);
        cmd.Parameters.AddWithValue("lo", codeLow);
        cmd.Parameters.AddWithValue("hi", codeHigh);
        cmd.Parameters.AddWithValue("code", accountCode);
        await cmd.ExecuteNonQueryAsync();

        return new SeededSpace(spaceId, groupId, accountId);
    }

    /// <summary>Seeds an empty space (no groups/accounts), as superuser.</summary>
    public async Task<Guid> SeedBareSpaceAsync()
    {
        var spaceId = Guid.NewGuid();
        await using var connection = await OpenSuperuserAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO spaces (id, name, base_currency, created_at) " +
            "VALUES (@sid, 'Bare Space', 'CHF', now());",
            connection);
        cmd.Parameters.AddWithValue("sid", spaceId);
        await cmd.ExecuteNonQueryAsync();
        return spaceId;
    }

    public async Task SeedPeriodAsync(Guid spaceId, DateOnly startDate, DateOnly endExclusive, string state = "open")
    {
        await using var connection = await OpenSuperuserAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO periods (id, space_id, name, start_date, end_exclusive, state, created_at) " +
            "VALUES (@id, @space, 'FY 2026', @start, @end, @state, now());",
            connection);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("space", spaceId);
        cmd.Parameters.AddWithValue("start", startDate);
        cmd.Parameters.AddWithValue("end", endExclusive);
        cmd.Parameters.AddWithValue("state", state);
        await cmd.ExecuteNonQueryAsync();
    }
}

public readonly record struct SeededSpace(Guid SpaceId, Guid GroupId, Guid AccountId);

[CollectionDefinition("Ledger schema")]
public sealed class LedgerDbCollectionDefinition : ICollectionFixture<LedgerDbFixture>;
