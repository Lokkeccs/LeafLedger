using Npgsql;
using Xunit;
using LeafLedger.IntegrationTests.Ledger;

namespace LeafLedger.IntegrationTests.Authorization;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class IdentityLinkResolutionTests
{
    private static readonly Guid SubjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly LedgerDbFixture _fixture;

    public IdentityLinkResolutionTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Resolving_the_same_identity_is_deterministic_and_link_only()
    {
        await using var app = await _fixture.OpenAppNoContextAsync();
        var first = await ResolveAsync(app, SubjectId, TenantId);
        var second = await ResolveAsync(app, SubjectId, TenantId);

        Assert.Equal(first, second);

        await using var superuser = await _fixture.OpenSuperuserAsync();
        await using var count = new NpgsqlCommand(
            "SELECT " +
            "(SELECT COUNT(*) FROM identity_links WHERE subject = @subject AND tenant_id = @tenant) AS links, " +
            "(SELECT COUNT(*) FROM memberships WHERE user_id = @user) AS memberships;",
            superuser);
        count.Parameters.AddWithValue("subject", SubjectId);
        count.Parameters.AddWithValue("tenant", TenantId);
        count.Parameters.AddWithValue("user", first);
        await using var reader = await count.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(0L, reader.GetInt64(1));
    }

    [Fact]
    public async Task App_role_can_resolve_but_cannot_enumerate_identity_links()
    {
        await using var app = await _fixture.OpenAppNoContextAsync();

        await using var enumerate = new NpgsqlCommand("SELECT user_id FROM identity_links;", app);
        await Assert.ThrowsAsync<PostgresException>(() => enumerate.ExecuteScalarAsync());

        var userId = await ResolveAsync(app, Guid.NewGuid(), TenantId);
        Assert.NotEqual(Guid.Empty, userId);
    }

    [Fact]
    public async Task Concurrent_first_contact_resolution_returns_one_internal_id()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var app = await _fixture.OpenAppNoContextAsync();
            return await ResolveAsync(app, SubjectId, TenantId);
        }));

        Assert.All(results, result => Assert.Equal(results[0], result));

        await using var superuser = await _fixture.OpenSuperuserAsync();
        await using var count = new NpgsqlCommand(
            "SELECT COUNT(*) FROM identity_links WHERE subject = @subject AND tenant_id = @tenant;",
            superuser);
        count.Parameters.AddWithValue("subject", SubjectId);
        count.Parameters.AddWithValue("tenant", TenantId);
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Different_subjects_or_tenants_receive_distinct_internal_ids()
    {
        await using var app = await _fixture.OpenAppNoContextAsync();
        var subjectVariant = await ResolveAsync(app, Guid.NewGuid(), TenantId);
        var tenantVariant = await ResolveAsync(app, SubjectId, Guid.NewGuid());

        Assert.NotEqual(subjectVariant, tenantVariant);
    }

    private static async Task<Guid> ResolveAsync(NpgsqlConnection connection, Guid subject, Guid tenant)
    {
        await using var command = new NpgsqlCommand(
            "SELECT resolve_identity_link(@subject, @tenant);",
            connection);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("tenant", tenant);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }
}