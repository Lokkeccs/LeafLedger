using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class GroupCodeRangeTests
{
    private readonly LedgerDbFixture _fixture;

    public GroupCodeRangeTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Overlapping_code_range_in_same_space_is_rejected()
    {
        // Seeded group occupies [1000, 2000).
        var space = await _fixture.SeedSpaceAsync();
        await using var connection = await _fixture.OpenAppAsync(space.SpaceId);

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => InsertGroupAsync(connection, space.SpaceId, 1500, 2500));
        Assert.Equal("23P01", ex.SqlState); // exclusion_violation
    }

    [Fact]
    public async Task Adjacent_non_overlapping_range_in_same_space_is_allowed()
    {
        var space = await _fixture.SeedSpaceAsync();
        await using var connection = await _fixture.OpenAppAsync(space.SpaceId);

        // [2000, 3000) abuts the seeded [1000, 2000) without overlapping (upper bound exclusive).
        var rows = await InsertGroupAsync(connection, space.SpaceId, 2000, 3000);
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Same_range_in_a_different_space_is_allowed()
    {
        // Space A owns [1000, 2000); space B is empty.
        var a = await _fixture.SeedSpaceAsync();
        var otherSpaceId = await _fixture.SeedBareSpaceAsync();
        await using var connection = await _fixture.OpenAppAsync(otherSpaceId);

        // The exclusion constraint is scoped by space_id, so the same range is fine here.
        var rows = await InsertGroupAsync(connection, otherSpaceId, 1000, 2000);
        Assert.Equal(1, rows);
        _ = a;
    }

    private static async Task<int> InsertGroupAsync(
        NpgsqlConnection connection, Guid spaceId, int low, int high)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO account_groups (id, space_id, code_range, name, created_at) " +
            "VALUES (@id, @sid, int4range(@lo, @hi), 'Group', now());",
            connection);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("sid", spaceId);
        cmd.Parameters.AddWithValue("lo", low);
        cmd.Parameters.AddWithValue("hi", high);
        return await cmd.ExecuteNonQueryAsync();
    }
}
