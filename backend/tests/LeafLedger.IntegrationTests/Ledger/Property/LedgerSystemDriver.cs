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

namespace LeafLedger.IntegrationTests.Ledger.FinancialProperties;

internal sealed class LedgerSystemDriver : IAsyncDisposable
{
    private readonly LedgerDbFixture _fixture;
    private readonly Guid _actor = Guid.NewGuid();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private LedgerSystemDriver(LedgerDbFixture fixture, WebApplicationFactory<Program> factory)
    {
        _fixture = fixture;
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Guid SpaceId { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid SecondAccountId { get; private set; }

    public static async Task<LedgerSystemDriver> CreateAsync(LedgerDbFixture fixture)
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseTestServer();
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:Postgres", fixture.ConnectionString);
                builder.ConfigureTestServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                        options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
                    }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.AuthenticationScheme, _ => { });
                });
            });

        var driver = new LedgerSystemDriver(fixture, factory);
        var space = await fixture.SeedSpaceAsync();
        driver.SecondAccountId = await driver.SeedAdditionalAccountAsync(space.SpaceId, space.GroupId, 1001);
        await fixture.SeedPeriodAsync(space.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        await fixture.SeedMembershipAsync(space.SpaceId, driver._actor, "Owner");
        driver.SpaceId = space.SpaceId;
        driver.AccountId = space.AccountId;
        return driver;
    }

    public async Task<SystemResponse> PostAsync(LedgerCommand.PostValid command)
    {
        return await SendPostAsync(new
        {
            date = command.Date,
            description = "WP08 generated valid post",
            lines = Lines(command.DebitAccountId, command.CreditAccountId, command.AmountMinor, -command.AmountMinor),
        }, command.IdempotencyKey);
    }

    public async Task<SystemResponse> PostUnbalancedAsync(LedgerCommand.PostUnbalanced command)
    {
        return await SendPostAsync(new
        {
            date = command.Date,
            description = "WP08 generated unbalanced post",
            lines = Lines(command.AccountId, command.AccountId, command.DebitAmountMinor, -command.CreditAmountMinor),
        }, command.IdempotencyKey);
    }

    public async Task<SystemResponse> GetIntegrityAsync()
    {
        return await SendAsync(HttpMethod.Get, $"/api/v1/spaces/{SpaceId}/integrity", null, null);
    }

    public async Task<SystemResponse> GetTrialBalanceAsync()
    {
        return await SendAsync(HttpMethod.Get, $"/api/v1/spaces/{SpaceId}/reports/trial-balance", null, null);
    }

    public async Task<IReadOnlyDictionary<Guid, long>> GetTrialBalanceByAccountAsync()
    {
        var response = await GetTrialBalanceAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Could not read trial balance: {response.StatusCode}");
        }

        return response.Body.GetProperty("lines")
            .EnumerateArray()
            .ToDictionary(
                line => line.GetProperty("accountId").GetGuid(),
                line => line.GetProperty("baseBalanceMinor").GetInt64());
    }

    public async Task<int> GetLiveEntryCountAsync()
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM journal_entries source " +
            "WHERE source.space_id = @space AND source.reverses_entry_id IS NULL " +
            "AND NOT EXISTS (SELECT 1 FROM journal_entries reversal " +
            "WHERE reversal.space_id = source.space_id AND reversal.reverses_entry_id = source.id);", connection);
        command.Parameters.AddWithValue("space", SpaceId);
        return checked((int)(long)(await command.ExecuteScalarAsync())!);
    }

    public async Task<SystemResponse> ReverseAsync(Guid entryId, DateOnly date, string idempotencyKey)
    {
        return await SendAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{SpaceId}/journal-entries/{entryId}/reverse",
            new { date },
            idempotencyKey);
    }

    public async Task<SystemResponse> LockSeedPeriodAsync()
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM periods WHERE space_id = @space ORDER BY start_date LIMIT 1;", connection);
        command.Parameters.AddWithValue("space", SpaceId);
        var periodId = (Guid)(await command.ExecuteScalarAsync())!;
        var closed = await SendAsync(HttpMethod.Post, $"/api/v1/spaces/{SpaceId}/periods/{periodId}/close", new { }, NewDeterministicKey(1));
        if (closed.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Could not close test period: {closed.Body}");
        }

        var reopened = await SendAsync(HttpMethod.Post, $"/api/v1/spaces/{SpaceId}/periods/{periodId}/reopen", new { }, NewDeterministicKey(2));
        if (reopened.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Could not reopen test period: {reopened.Body}");
        }

        var locked = await SendAsync(HttpMethod.Post, $"/api/v1/spaces/{SpaceId}/periods/{periodId}/lock", new { }, NewDeterministicKey(3));
        if (locked.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Could not lock test period: {locked.Body}");
        }

        return locked;
    }

    public async Task CloseSeedPeriodAsync()
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM periods WHERE space_id = @space ORDER BY start_date LIMIT 1;", connection);
        command.Parameters.AddWithValue("space", SpaceId);
        var periodId = (Guid)(await command.ExecuteScalarAsync())!;
        var response = await SendAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{SpaceId}/periods/{periodId}/close",
            new { },
            PropertyRunner.DeterministicKey(4));
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Could not close test period: {response.Body}");
        }
    }

    public async Task<long> GetMaxEntryNumberAsync()
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COALESCE(MAX(entry_no), 0) FROM journal_entries WHERE space_id = @space;", connection);
        command.Parameters.AddWithValue("space", SpaceId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> SeedAdditionalAccountAsync(Guid spaceId, Guid groupId, int accountCode)
    {
        var accountId = Guid.NewGuid();
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO accounts (id, space_id, group_id, code, name, currency, kind, is_active, created_at) " +
            "VALUES (@id, @space, @group, @code, 'Second account', 'CHF', 'asset', true, now());", connection);
        command.Parameters.AddWithValue("id", accountId);
        command.Parameters.AddWithValue("space", spaceId);
        command.Parameters.AddWithValue("group", groupId);
        command.Parameters.AddWithValue("code", accountCode);
        await command.ExecuteNonQueryAsync();
        return accountId;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<SystemResponse> SendPostAsync(object payload, string idempotencyKey)
    {
        return await SendAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{SpaceId}/journal-entries",
            payload,
            idempotencyKey);
    }

    private async Task<SystemResponse> SendAsync(
        HttpMethod method,
        string route,
        object? payload,
        string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(method, route);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        request.Headers.Add("X-Test-Subject", _actor.ToString());
        request.Headers.Add("X-Test-Scope", "ledger.write");
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        using var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var body = string.IsNullOrWhiteSpace(content)
            ? default
            : JsonSerializer.Deserialize<JsonElement>(content);
        return new SystemResponse(response.StatusCode, body, response.Headers.Contains("Idempotent-Replayed"));
    }

    private static object[] Lines(Guid debitAccountId, Guid creditAccountId, long debit, long credit) =>
    [
        new { accountId = debitAccountId, amountMinor = debit, currency = "CHF", baseAmountMinor = debit },
        new { accountId = creditAccountId, amountMinor = credit, currency = "CHF", baseAmountMinor = credit },
    ];

    private static string NewDeterministicKey(int value) => new Ulid(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)value }).ToString();
}

internal readonly record struct SystemResponse(
    HttpStatusCode StatusCode,
    JsonElement Body,
    bool IdempotentReplayed);