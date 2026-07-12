using LeafLedger.Modules.Ledger.Infrastructure;
using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class IdempotencyCleanupTests
{
    private readonly LedgerDbFixture _fixture;

    public IdempotencyCleanupTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Cleanup_pass_deletes_expired_rows_and_keeps_fresh_rows()
    {
        var expiredKey = Guid.NewGuid();
        var freshKey = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        var actor = Guid.NewGuid();

        await using (var connection = await _fixture.OpenSuperuserAsync())
        await using (var command = new NpgsqlCommand(
            "INSERT INTO idempotency_keys " +
            "(space_id, idempotency_key, actor_id, target, request_hash, response_status, response_body, created_at) " +
            "VALUES (@space, @expired, @actor, 'post', @hash, 201, '{}'::jsonb, now() - interval '25 hours'), " +
            "(@space, @fresh, @actor, 'post', @hash, 201, '{}'::jsonb, now());", connection))
        {
            command.Parameters.AddWithValue("space", space);
            command.Parameters.AddWithValue("expired", expiredKey);
            command.Parameters.AddWithValue("fresh", freshKey);
            command.Parameters.AddWithValue("actor", actor);
            command.Parameters.AddWithValue("hash", new byte[32]);
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal(1, await IdempotencyCleanupService.RunCleanupPassAsync(_fixture.ConnectionString));

        await using var verify = await _fixture.OpenSuperuserAsync();
        await using var query = new NpgsqlCommand(
            "SELECT idempotency_key FROM idempotency_keys WHERE space_id = @space ORDER BY idempotency_key;", verify);
        query.Parameters.AddWithValue("space", space);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(freshKey, reader.GetGuid(0));
        Assert.False(await reader.ReadAsync());

        var appExpiredKey = Guid.NewGuid();
        await using (var seed = await _fixture.OpenSuperuserAsync())
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO idempotency_keys " +
            "(space_id, idempotency_key, actor_id, target, request_hash, response_status, response_body, created_at) " +
            "VALUES (@space, @key, @actor, 'post', @hash, 201, '{}'::jsonb, now() - interval '25 hours');", seed))
        {
            insert.Parameters.AddWithValue("space", space);
            insert.Parameters.AddWithValue("key", appExpiredKey);
            insert.Parameters.AddWithValue("actor", actor);
            insert.Parameters.AddWithValue("hash", new byte[32]);
            await insert.ExecuteNonQueryAsync();
        }

        await using var app = await _fixture.OpenAppAsync(space);
        await using var appCleanup = new NpgsqlCommand(
            "SELECT delete_expired_idempotency_keys();", app);
        Assert.Equal(1, (int)(await appCleanup.ExecuteScalarAsync())!);
    }
}