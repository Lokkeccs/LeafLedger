using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeafLedger.IntegrationTests.Authorization;
using LeafLedger.Host.Authorization;
using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class AccountCatalogEndpointTests : IAsyncLifetime
{
    private readonly LedgerDbFixture _fixture;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public AccountCatalogEndpointTests(LedgerDbFixture fixture) => _fixture = fixture;

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
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Viewer_receives_accounts_ordered_by_code()
    {
        var subject = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync(codeLow: 1000, codeHigh: 3000, accountCode: 2000);
        await _fixture.SeedMembershipAsync(space.SpaceId, subject, "Viewer");
        await AddAccountAsync(space.SpaceId, space.GroupId, 1000, "Bank");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/spaces/{space.SpaceId}/accounts");
        request.Headers.Add("X-Test-Subject", subject.ToString());
        request.Headers.Add("X-Test-Scope", "ledger.write");
        using var response = await _client!.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accounts = body.GetProperty("accounts");
        Assert.Equal(2, accounts.GetArrayLength());
        Assert.Equal(1000, accounts[0].GetProperty("code").GetInt32());
        Assert.Equal("Bank", accounts[0].GetProperty("name").GetString());
        Assert.Equal(2000, accounts[1].GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        var space = await _fixture.SeedBareSpaceAsync();

        using var response = await SendGetAsync(space, null, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Missing_required_scope_returns_403()
    {
        var subject = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, subject, "Viewer");

        using var response = await SendGetAsync(space.SpaceId, subject, "other.scope");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_with_no_accounts_receives_an_empty_list()
    {
        var subject = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(space, subject, "Viewer");

        using var response = await SendGetAsync(space, subject, "ledger.write");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(body.GetProperty("accounts").EnumerateArray());
    }

    [Fact]
    public async Task Rls_hides_accounts_from_a_second_space()
    {
        var subject = Guid.NewGuid();
        var firstSpace = await _fixture.SeedSpaceAsync(accountCode: 1000);
        var secondSpace = await _fixture.SeedSpaceAsync(accountCode: 2000);
        await _fixture.SeedMembershipAsync(firstSpace.SpaceId, subject, "Viewer");
        await _fixture.SeedMembershipAsync(secondSpace.SpaceId, subject, "Viewer");

        using var response = await SendGetAsync(firstSpace.SpaceId, subject, "ledger.write");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accounts = body.GetProperty("accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(accounts.EnumerateArray());
        Assert.Equal(1000, accounts[0].GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Development_seed_is_idempotent_and_creates_demo_catalog()
    {
        var devUserId = Guid.NewGuid();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fixture.ConnectionString,
                ["Seed:Enabled"] = "true",
                ["Seed:DevUserId"] = devUserId.ToString(),
                ["Seed:SpaceId"] = "8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8f8a1",
            })
            .Build();

        await _factory!.Services.SeedAsync(configuration);
        await _factory.Services.SeedAsync(configuration);

        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "SELECT " +
            "(SELECT COUNT(*) FROM spaces WHERE id = @space), " +
            "(SELECT COUNT(*) FROM accounts WHERE space_id = @space), " +
            "(SELECT COUNT(*) FROM periods WHERE space_id = @space AND state = 'open'), " +
            "(SELECT COUNT(*) FROM memberships WHERE space_id = @space AND user_id = @user);",
            connection);
        command.Parameters.AddWithValue("space", Guid.Parse("8f8f31e1-5cf4-4d87-a4ef-4f2aa1f8f8a1"));
        command.Parameters.AddWithValue("user", devUserId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(3L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
        Assert.Equal(1L, reader.GetInt64(3));
    }

    private async Task<HttpResponseMessage> SendGetAsync(Guid spaceId, Guid? subject, string? scope)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/spaces/{spaceId}/accounts");
        if (subject is Guid authenticatedSubject)
        {
            request.Headers.Add("X-Test-Subject", authenticatedSubject.ToString());
        }

        if (scope is not null)
        {
            request.Headers.Add("X-Test-Scope", scope);
        }

        return await _client!.SendAsync(request);
    }

    private async Task AddAccountAsync(Guid spaceId, Guid groupId, int code, string name)
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO accounts (id, space_id, group_id, code, name, currency, kind, is_active, created_at) " +
            "VALUES (@id, @space, @group, @code, @name, 'CHF', 'asset', true, now());",
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("space", spaceId);
        command.Parameters.AddWithValue("group", groupId);
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("name", name);
        await command.ExecuteNonQueryAsync();
    }
}