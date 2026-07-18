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
public sealed class AccountLedgerEndpointTests : IAsyncLifetime
{
    private readonly LedgerDbFixture _fixture;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public AccountLedgerEndpointTests(LedgerDbFixture fixture) => _fixture = fixture;

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
    public async Task Viewer_receives_ordered_lines_and_running_balances()
    {
        var viewer = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(space, viewer, "Viewer");
        var ledger = await SeedLedgerAsync(space);

        using var response = await SendGetAsync(space, ledger.AccountId, viewer, "ledger.write");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2000, body.GetProperty("accountCode").GetInt32());
        Assert.Equal("Cash", body.GetProperty("accountName").GetString());
        Assert.Equal("asset", body.GetProperty("accountKind").GetString());
        Assert.Equal("CHF", body.GetProperty("accountCurrency").GetString());
        Assert.Equal(3, body.GetProperty("lines").GetArrayLength());
        Assert.Equal(new long[] { 100, 70, 120 }, body.GetProperty("lines").EnumerateArray()
            .Select(line => line.GetProperty("runningBalanceMinor").GetInt64()).ToArray());
        Assert.Equal(120, body.GetProperty("closingBalanceMinor").GetInt64());
        Assert.Equal(ledger.FirstEntryId, body.GetProperty("lines")[0].GetProperty("entryId").GetGuid());
        Assert.Equal(ledger.SecondEntryId, body.GetProperty("lines")[1].GetProperty("entryId").GetGuid());
        Assert.Equal(1, body.GetProperty("lines")[0].GetProperty("entryNo").GetInt64());
        Assert.Equal("2026-01-01", body.GetProperty("lines")[0].GetProperty("date").GetString());
        Assert.Equal("Opening", body.GetProperty("lines")[0].GetProperty("description").GetString());
        Assert.Equal("REF-1", body.GetProperty("lines")[0].GetProperty("reference").GetString());
        Assert.Equal(100, body.GetProperty("lines")[0].GetProperty("amountMinor").GetInt64());
        Assert.Equal(100, body.GetProperty("lines")[0].GetProperty("baseAmountMinor").GetInt64());
        Assert.Equal("CHF", body.GetProperty("lines")[0].GetProperty("lineCurrency").GetString());
    }

    [Fact]
    public async Task Date_window_uses_brought_forward_opening_balance_and_inclusive_boundaries()
    {
        var viewer = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(space, viewer, "Viewer");
        var ledger = await SeedLedgerAsync(space);

        using var response = await SendGetAsync(space, ledger.AccountId, viewer, "ledger.write", "2026-01-02", "2026-01-03");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100, body.GetProperty("openingBalanceMinor").GetInt64());
        Assert.Equal(2, body.GetProperty("lines").GetArrayLength());
        Assert.Equal(new long[] { 70, 120 }, body.GetProperty("lines").EnumerateArray()
            .Select(line => line.GetProperty("runningBalanceMinor").GetInt64()).ToArray());
        Assert.Equal(120, body.GetProperty("closingBalanceMinor").GetInt64());
    }

    [Fact]
    public async Task Empty_or_foreign_account_returns_zeroed_empty_report()
    {
        var viewer = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(space, viewer, "Viewer");

        using var response = await SendGetAsync(space, Guid.NewGuid(), viewer, "ledger.write");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(body.GetProperty("lines").EnumerateArray());
        Assert.Equal(0, body.GetProperty("openingBalanceMinor").GetInt64());
        Assert.Equal(0, body.GetProperty("closingBalanceMinor").GetInt64());
    }

    [Fact]
    public async Task Rls_hides_lines_from_a_second_space()
    {
        var viewer = Guid.NewGuid();
        var firstSpace = await _fixture.SeedBareSpaceAsync();
        var secondSpace = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(firstSpace, viewer, "Viewer");
        var secondLedger = await SeedLedgerAsync(secondSpace);

        using var response = await SendGetAsync(firstSpace, secondLedger.AccountId, viewer, "ledger.write");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(body.GetProperty("lines").EnumerateArray());
        Assert.Equal(0, body.GetProperty("openingBalanceMinor").GetInt64());
        Assert.Equal(0, body.GetProperty("closingBalanceMinor").GetInt64());
    }

    [Fact]
    public async Task Anonymous_request_returns_401_and_missing_scope_returns_403()
    {
        var viewer = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(space, viewer, "Viewer");

        using var anonymous = await SendGetAsync(space, Guid.NewGuid(), null, null);
        using var missingScope = await SendGetAsync(space, Guid.NewGuid(), viewer, "other.scope");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missingScope.StatusCode);
    }

    private async Task<HttpResponseMessage> SendGetAsync(
        Guid spaceId,
        Guid accountId,
        Guid? subject,
        string? scope,
        string? from = null,
        string? to = null)
    {
        var query = from is null && to is null ? string.Empty : $"?from={from}&to={to}";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/spaces/{spaceId}/reports/account-ledger/{accountId}{query}");
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

    private async Task<SeededLedger> SeedLedgerAsync(Guid spaceId)
    {
        var groupId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var firstEntryId = Guid.NewGuid();
        var secondEntryId = Guid.NewGuid();
        var balancingAccountId = Guid.NewGuid();

        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO account_groups (id, space_id, code_range, name, created_at) VALUES (@group, @space, int4range(2000, 2100), 'Ledger tests', now()); " +
            "INSERT INTO accounts (id, space_id, group_id, code, name, currency, kind, is_active, created_at) VALUES " +
            "(@account, @space, @group, 2000, 'Cash', 'CHF', 'asset', true, now()), " +
            "(@balancing, @space, @group, 2001, 'Equity', 'CHF', 'equity', true, now()); " +
            "INSERT INTO journal_entries (id, space_id, entry_no, date, status, description, reference, created_by, created_at) VALUES " +
            "(@firstEntry, @space, 1, DATE '2026-01-01', 'posted', 'Opening', 'REF-1', @actor, now()), " +
            "(@secondEntry, @space, 2, DATE '2026-01-02', 'posted', 'Receipt', 'REF-2', @actor, now()), " +
            "(@thirdEntry, @space, 3, DATE '2026-01-03', 'posted', 'Reversal', 'REF-3', @actor, now()); " +
            "INSERT INTO journal_lines (id, entry_id, space_id, account_id, amount_minor, currency, base_amount_minor) VALUES " +
            "(@line1, @firstEntry, @space, @account, 100, 'CHF', 100), (@line2, @firstEntry, @space, @balancing, -100, 'CHF', -100), " +
            "(@line3, @secondEntry, @space, @account, -30, 'CHF', -30), (@line4, @secondEntry, @space, @balancing, 30, 'CHF', 30), " +
            "(@line5, @thirdEntry, @space, @account, 50, 'CHF', 50), (@line6, @thirdEntry, @space, @balancing, -50, 'CHF', -50);",
            connection);
        command.Parameters.AddWithValue("group", groupId);
        command.Parameters.AddWithValue("space", spaceId);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("balancing", balancingAccountId);
        command.Parameters.AddWithValue("firstEntry", firstEntryId);
        command.Parameters.AddWithValue("secondEntry", secondEntryId);
        command.Parameters.AddWithValue("thirdEntry", Guid.NewGuid());
        command.Parameters.AddWithValue("line1", Guid.NewGuid());
        command.Parameters.AddWithValue("line2", Guid.NewGuid());
        command.Parameters.AddWithValue("line3", Guid.NewGuid());
        command.Parameters.AddWithValue("line4", Guid.NewGuid());
        command.Parameters.AddWithValue("line5", Guid.NewGuid());
        command.Parameters.AddWithValue("line6", Guid.NewGuid());
        command.Parameters.AddWithValue("actor", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
        return new SeededLedger(accountId, firstEntryId, secondEntryId);
    }

    private sealed record SeededLedger(Guid AccountId, Guid FirstEntryId, Guid SecondEntryId);
}