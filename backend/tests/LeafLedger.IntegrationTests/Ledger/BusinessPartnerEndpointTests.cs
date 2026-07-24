using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeafLedger.IntegrationTests.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class BusinessPartnerEndpointTests : IAsyncLifetime
{
    private static int _keySequence;
    private readonly LedgerDbFixture _fixture;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public BusinessPartnerEndpointTests(LedgerDbFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestServer();
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Postgres", _fixture.ConnectionString);
            builder.ConfigureTestServices(services => services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                    options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.AuthenticationScheme, _ => { }));
        });
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Member_can_create_partner_with_inert_fields_and_replay_it()
    {
        var member = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, member, "Member");
        var payload = new
        {
            name = "Acme AG",
            type = "customer",
            isActive = true,
            partnerNumber = "BP-100",
            countryCode = "ch",
            notes = "Primary customer",
        };
        var key = NewKey();

        using var first = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId), payload, member, key);
        var firstBody = await ReadJsonAsync(first);
        using var replay = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId), payload, member, key);
        var replayBody = await ReadJsonAsync(replay);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("BP-100", firstBody.GetProperty("partnerNumber").GetString());
        Assert.Equal("CH", firstBody.GetProperty("countryCode").GetString());
        Assert.Equal("Primary customer", firstBody.GetProperty("notes").GetString());
        Assert.False(string.IsNullOrWhiteSpace(firstBody.GetProperty("version").GetString()));
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(firstBody.GetProperty("id").GetGuid(), replayBody.GetProperty("id").GetGuid());
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM business_partners WHERE space_id = @space AND partner_number = 'BP-100';",
            ("space", space.SpaceId)));
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM audit_log WHERE table_name = 'business_partners' AND row_id = @id AND action = 'INSERT';",
            ("id", firstBody.GetProperty("id").GetGuid())));
    }

    [Fact]
    public async Task Duplicate_name_and_partner_number_return_structured_422s()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        using var first = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId),
            new { name = "Duplicate", type = "customer", partnerNumber = "BP-200" }, owner, NewKey());
        using var duplicateName = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId),
            new { name = "Duplicate", type = "vendor", partnerNumber = "BP-201" }, owner, NewKey());
        using var duplicateNumber = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId),
            new { name = "Another", type = "vendor", partnerNumber = "BP-200" }, owner, NewKey());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("partner.name_taken", await FirstIssueCodeAsync(duplicateName));
        Assert.Equal("partner.number_taken", await FirstIssueCodeAsync(duplicateNumber));
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_with_different_payload_returns_409()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        var key = NewKey();
        using var first = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId),
            new { name = "Original", type = "customer" }, owner, key);
        using var collision = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId),
            new { name = "Different", type = "customer" }, owner, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        Assert.Equal("idempotency.key_reused", await FirstIssueCodeAsync(collision));
    }

    [Theory]
    [InlineData("type_invalid", "name", "invalid", null, null)]
    [InlineData("country_invalid", "name", "customer", "CHE", null)]
    [InlineData("country_invalid", "name", "customer", "ÄA", null)]
    [InlineData("country_invalid", "name", "customer", "A_", null)]
    [InlineData("validity_range_invalid", "name", "customer", null, "2025-01-01")]
    public async Task Invalid_partner_input_returns_stable_422(string expectedCode, string name, string type, string? country, string? validTo)
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        var payload = new
        {
            name,
            type,
            isActive = true,
            countryCode = country,
            validFrom = validTo is null ? null : "2026-01-01",
            validTo,
        };

        using var response = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId), payload, owner, NewKey());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal($"partner.{expectedCode}", await FirstIssueCodeAsync(response));
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    [InlineData("Member")]
    public async Task Owner_admin_and_member_can_create_partners(string role)
    {
        var actor = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, actor, role);

        using var response = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId),
            new { name = $"{role} partner", type = "customer" }, actor, NewKey());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Write_requires_a_valid_idempotency_key()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");

        using var missing = await SendJsonWithoutIdempotencyKeyAsync(HttpMethod.Post, PartnerPath(space.SpaceId),
            new { name = "Missing key", type = "customer" }, owner);
        using var invalid = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId),
            new { name = "Invalid key", type = "customer" }, owner, "not-a-ulid");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("idempotency.key_required", await ProblemCodeAsync(missing));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("idempotency.key_invalid", await ProblemCodeAsync(invalid));
    }

    [Fact]
    public async Task Update_returns_new_version_and_stale_update_returns_current_state()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        using var create = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId),
            new { name = "Before", type = "vendor", isActive = true }, owner, NewKey());
        var created = await ReadJsonAsync(create);
        var partnerId = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetString();

        using var update = await SendJsonAsync(HttpMethod.Patch, PartnerPath(space.SpaceId, partnerId),
            new { name = "After", type = "vendor", isActive = false, version }, owner, NewKey());
        var updated = await ReadJsonAsync(update);
        using var stale = await SendJsonAsync(HttpMethod.Patch, PartnerPath(space.SpaceId, partnerId),
            new { name = "Lost update", type = "vendor", isActive = true, version }, owner, NewKey());
        var staleBody = await ReadJsonAsync(stale);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.False(updated.GetProperty("isActive").GetBoolean());
        Assert.NotEqual(version, updated.GetProperty("version").GetString());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("partner.version_conflict", staleBody.GetProperty("errors")[0].GetProperty("code").GetString());
        var current = staleBody.GetProperty("errors")[0].GetProperty("current");
        Assert.Equal("After", current.GetProperty("name").GetString());
        Assert.False(current.GetProperty("isActive").GetBoolean());
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM audit_log WHERE table_name = 'business_partners' AND row_id = @id AND action = 'UPDATE';",
            ("id", partnerId)));
    }

    [Fact]
    public async Task Reads_are_rls_scoped_and_viewer_cannot_write()
    {
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var first = await _fixture.SeedSpaceAsync();
        var second = await _fixture.SeedSpaceAsync(accountCode: 2000);
        await _fixture.SeedMembershipAsync(first.SpaceId, firstOwner, "Owner");
        await _fixture.SeedMembershipAsync(second.SpaceId, secondOwner, "Owner");
        await _fixture.SeedMembershipAsync(first.SpaceId, viewer, "Viewer");
        using var firstCreate = await SendJsonAsync(HttpMethod.Post, PartnerPath(first.SpaceId),
            new { name = "First partner", type = "customer" }, firstOwner, NewKey());
        using var secondCreate = await SendJsonAsync(HttpMethod.Post, PartnerPath(second.SpaceId),
            new { name = "Second partner", type = "customer" }, secondOwner, NewKey());

        using var firstList = await SendGetAsync(PartnerPath(first.SpaceId), viewer, "ledger.write");
        using var viewerWrite = await SendJsonAsync(HttpMethod.Post, PartnerPath(first.SpaceId),
            new { name = "Denied", type = "customer" }, viewer, NewKey());
        using var anonymousWrite = await SendJsonAsync(HttpMethod.Post, PartnerPath(first.SpaceId),
            new { name = "Anonymous", type = "customer" }, null, NewKey());
        var listBody = await ReadJsonAsync(firstList);

        Assert.Equal(HttpStatusCode.Created, firstCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondCreate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstList.StatusCode);
        Assert.Single(listBody.GetProperty("partners").EnumerateArray());
        Assert.Equal("First partner", listBody.GetProperty("partners")[0].GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, viewerWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousWrite.StatusCode);
    }

    [Fact]
    public async Task Single_partner_read_supports_not_found_and_activation_round_trip()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        using var create = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId),
            new { name = "Lifecycle partner", type = "customer", isActive = true }, owner, NewKey());
        var created = await ReadJsonAsync(create);
        var partnerId = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetString();

        using var read = await SendGetAsync(PartnerPath(space.SpaceId, partnerId), owner, "ledger.write");
        using var deactivate = await SendJsonAsync(HttpMethod.Patch, PartnerPath(space.SpaceId, partnerId),
            new { name = "Lifecycle partner", type = "customer", isActive = false, version }, owner, NewKey());
        var deactivated = await ReadJsonAsync(deactivate);
        using var reactivate = await SendJsonAsync(HttpMethod.Patch, PartnerPath(space.SpaceId, partnerId),
            new { name = "Lifecycle partner", type = "customer", isActive = true, version = deactivated.GetProperty("version").GetString() }, owner, NewKey());
        var reactivated = await ReadJsonAsync(reactivate);
        using var missing = await SendGetAsync(PartnerPath(space.SpaceId, Guid.NewGuid()), owner, "ledger.write");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(partnerId, (await ReadJsonAsync(read)).GetProperty("id").GetGuid());
        Assert.False(deactivated.GetProperty("isActive").GetBoolean());
        Assert.True(reactivated.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Write_to_another_space_is_rejected()
    {
        var owner = Guid.NewGuid();
        var first = await _fixture.SeedSpaceAsync();
        var second = await _fixture.SeedSpaceAsync(accountCode: 2000);
        await _fixture.SeedMembershipAsync(first.SpaceId, owner, "Owner");

        using var response = await SendJsonAsync(HttpMethod.Post, PartnerPath(second.SpaceId),
            new { name = "Cross-space", type = "customer" }, owner, NewKey());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task App_role_cannot_insert_a_partner_for_a_different_space()
    {
        var boundSpace = await _fixture.SeedSpaceAsync();
        var foreignSpace = await _fixture.SeedSpaceAsync();

        await using var connection = await _fixture.OpenAppAsync(boundSpace.SpaceId);
        await using var command = new NpgsqlCommand(
            "INSERT INTO business_partners (id, space_id, name, type, is_active, created_by, created_at, updated_at) " +
            "VALUES (@id, @foreignSpace, 'Foreign partner', 'customer', true, @actor, now(), now());",
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("foreignSpace", foreignSpace.SpaceId);
        command.Parameters.AddWithValue("actor", Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal("42501", exception.SqlState);
    }

    [Fact]
    public async Task Delete_rejects_referenced_partner_and_allows_unreferenced_partner()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        using var referencedCreate = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId),
            new { name = "Referenced", type = "vendor" }, owner, NewKey());
        var referencedId = (await ReadJsonAsync(referencedCreate)).GetProperty("id").GetGuid();
        await SeedJournalLineReferenceAsync(space, referencedId);
        using var blocked = await SendDeleteAsync(PartnerPath(space.SpaceId, referencedId), owner, NewKey());
        using var freeCreate = await SendJsonAsync(HttpMethod.Post, PartnerPath(space.SpaceId),
            new { name = "Free", type = "customer" }, owner, NewKey());
        var freeId = (await ReadJsonAsync(freeCreate)).GetProperty("id").GetGuid();
        using var deleted = await SendDeleteAsync(PartnerPath(space.SpaceId, freeId), owner, NewKey());

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("partner.in_use", await FirstIssueCodeAsync(blocked));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM business_partners WHERE id = @id;",
            ("id", referencedId)));
        Assert.Equal(0L, await ScalarAsync(
            "SELECT count(*) FROM business_partners WHERE id = @id;",
            ("id", freeId)));
    }

    private async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string path, object payload, Guid? subject, string key)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(payload) };
        AddAuth(request, subject, method == HttpMethod.Get ? "ledger.read" : "ledger.write");
        request.Headers.Add("Idempotency-Key", key);
        return await _client!.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendJsonWithoutIdempotencyKeyAsync(HttpMethod method, string path, object payload, Guid subject)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(payload) };
        AddAuth(request, subject, "ledger.write");
        return await _client!.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendDeleteAsync(string path, Guid subject, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        AddAuth(request, subject, "ledger.write");
        request.Headers.Add("Idempotency-Key", key);
        return await _client!.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendGetAsync(string path, Guid subject, string scope)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddAuth(request, subject, scope);
        return await _client!.SendAsync(request);
    }

    private static void AddAuth(HttpRequestMessage request, Guid? subject, string? scope)
    {
        if (subject is Guid authenticatedSubject) request.Headers.Add("X-Test-Subject", authenticatedSubject.ToString());
        if (scope is not null) request.Headers.Add("X-Test-Scope", scope);
    }

    private async Task SeedJournalLineReferenceAsync(SeededSpace space, Guid partnerId)
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO journal_entries (id, space_id, entry_no, date, status, created_by, created_at) " +
            "VALUES (@entry, @space, (SELECT coalesce(max(entry_no), 0) + 1 FROM journal_entries WHERE space_id = @space), DATE '2026-01-01', 'posted', @actor, now()); " +
            "INSERT INTO journal_lines (id, entry_id, space_id, account_id, amount_minor, currency, base_amount_minor, business_partner_id) " +
            "VALUES (@line, @entry, @space, @account, 100, 'CHF', 100, @partner), " +
            "(@balancingLine, @entry, @space, @account, -100, 'CHF', -100, NULL);", connection);
        command.Parameters.AddWithValue("entry", Guid.NewGuid());
        command.Parameters.AddWithValue("line", Guid.NewGuid());
        command.Parameters.AddWithValue("balancingLine", Guid.NewGuid());
        command.Parameters.AddWithValue("space", space.SpaceId);
        command.Parameters.AddWithValue("actor", Guid.NewGuid());
        command.Parameters.AddWithValue("account", space.AccountId);
        command.Parameters.AddWithValue("partner", partnerId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> FirstIssueCodeAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.GetProperty("errors")[0].GetProperty("code").GetString()!;
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.GetProperty("code").GetString()!;
    }

    private static string PartnerPath(Guid spaceId, Guid? partnerId = null) =>
        $"/api/v1/spaces/{spaceId}/partners{(partnerId is null ? "/" : $"/{partnerId}")}";

    private static string NewKey() => $"01J000000000000000000000{Interlocked.Increment(ref _keySequence):00}";
}
