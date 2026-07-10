using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class RlsTenancyTests
{
    private readonly LedgerDbFixture _fixture;

    public RlsTenancyTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task App_role_only_sees_rows_of_the_bound_space()
    {
        var a = await _fixture.SeedSpaceAsync();
        var b = await _fixture.SeedSpaceAsync();

        await using var connection = await _fixture.OpenAppAsync(a.SpaceId);
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM accounts;", connection);
        var visible = (long)(await cmd.ExecuteScalarAsync())!;

        // Only space A's single account is visible; space B is filtered out (fail-closed).
        Assert.Equal(1, visible);

        await using var idCmd = new NpgsqlCommand("SELECT space_id FROM accounts;", connection);
        var visibleSpace = (Guid)(await idCmd.ExecuteScalarAsync())!;
        Assert.Equal(a.SpaceId, visibleSpace);
        Assert.NotEqual(b.SpaceId, visibleSpace);
    }

    [Fact]
    public async Task App_role_cannot_insert_a_row_for_a_different_space()
    {
        var a = await _fixture.SeedSpaceAsync();
        var b = await _fixture.SeedSpaceAsync();

        await using var connection = await _fixture.OpenAppAsync(a.SpaceId);
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO memberships (id, space_id, user_id, role, created_at) " +
            "VALUES (@id, @foreignSpace, @user, 'viewer', now());",
            connection);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("foreignSpace", b.SpaceId);
        cmd.Parameters.AddWithValue("user", Guid.NewGuid());

        // WITH CHECK on the RLS policy rejects a cross-tenant write.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState);
    }

    [Fact]
    public async Task App_role_with_no_space_context_is_fail_closed()
    {
        var a = await _fixture.SeedSpaceAsync();

        await using var connection = await _fixture.OpenAppNoContextAsync();

        // No app.current_space_id GUC ⇒ USING (space_id = NULL) ⇒ 0 rows visible.
        await using (var selectCmd = new NpgsqlCommand("SELECT count(*) FROM accounts;", connection))
        {
            var visible = (long)(await selectCmd.ExecuteScalarAsync())!;
            Assert.Equal(0, visible);
        }

        // ...and a write is rejected by WITH CHECK (space_id = NULL is not true), even when
        // it names a real space id.
        await using var insertCmd = new NpgsqlCommand(
            "INSERT INTO memberships (id, space_id, user_id, role, created_at) " +
            "VALUES (@id, @sid, @user, 'viewer', now());",
            connection);
        insertCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        insertCmd.Parameters.AddWithValue("sid", a.SpaceId);
        insertCmd.Parameters.AddWithValue("user", Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<PostgresException>(() => insertCmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState);
    }
}
