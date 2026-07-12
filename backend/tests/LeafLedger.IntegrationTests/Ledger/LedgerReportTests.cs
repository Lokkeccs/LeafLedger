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
public sealed class LedgerReportTests : IAsyncLifetime
{
    private static readonly long[] ExpectedBalanceSheetAmounts = [100L, 40L, 10L, 50L];
    private static readonly long[] ExpectedIncomeStatementAmounts = [80L, 30L, 50L];

    private readonly LedgerDbFixture _fixture;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public LedgerReportTests(LedgerDbFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseTestServer();
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:Postgres", _fixture.ConnectionString);
                builder.ConfigureTestServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                        options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
                    }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.AuthenticationScheme, _ => { });
                });
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
    public async Task Viewer_can_read_all_reports_and_integrity_is_stable()
    {
        var viewer = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, viewer, "Viewer");

        foreach (var route in new[]
        {
            $"/api/v1/spaces/{space.SpaceId}/reports/trial-balance",
            $"/api/v1/spaces/{space.SpaceId}/reports/balance-sheet",
            $"/api/v1/spaces/{space.SpaceId}/reports/income-statement",
            $"/api/v1/spaces/{space.SpaceId}/integrity",
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Add("X-Test-Subject", viewer.ToString());
            request.Headers.Add("X-Test-Scope", "ledger.write");
            using var response = await _client!.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var first = await GetAsync($"/api/v1/spaces/{space.SpaceId}/integrity", viewer);
        using var second = await GetAsync($"/api/v1/spaces/{space.SpaceId}/integrity", viewer);
        var firstHash = await first.Content.ReadAsStringAsync();
        var secondHash = await second.Content.ReadAsStringAsync();
        Assert.Equal(firstHash, secondHash);
    }

    [Fact]
    public async Task Reports_apply_the_C1_sign_convention_and_net_result()
    {
        var viewer = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, viewer, "Viewer");
        var accounts = await SeedReportLedgerAsync(space.SpaceId);

        using var trialBalance = await GetAsync($"/api/v1/spaces/{space.SpaceId}/reports/trial-balance", viewer);
        var trial = await trialBalance.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, trial.GetProperty("totalBaseBalanceMinor").GetInt64());
        Assert.Equal(5, trial.GetProperty("lines").GetArrayLength());

        using var balanceSheet = await GetAsync($"/api/v1/spaces/{space.SpaceId}/reports/balance-sheet", viewer);
        var balance = await balanceSheet.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(50, balance.GetProperty("currentResultMinor").GetInt64());
        Assert.Equal(ExpectedBalanceSheetAmounts,
            balance.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("amountMinor").GetInt64()).ToArray());
        Assert.Equal("Current result", balance.GetProperty("lines")[3].GetProperty("name").GetString());

        using var incomeStatement = await GetAsync($"/api/v1/spaces/{space.SpaceId}/reports/income-statement", viewer);
        var income = await incomeStatement.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(50, income.GetProperty("netResultMinor").GetInt64());
        Assert.Equal(ExpectedIncomeStatementAmounts,
            income.GetProperty("lines").EnumerateArray().Select(line => line.GetProperty("amountMinor").GetInt64()).ToArray());
        Assert.Equal("Net result", income.GetProperty("lines")[2].GetProperty("name").GetString());
        Assert.Equal(accounts.IncomeId, income.GetProperty("lines")[0].GetProperty("accountId").GetGuid());
    }

    [Fact]
    public async Task Report_authorization_rejects_unauthenticated_non_member_and_cross_space_reads()
    {
        var member = Guid.NewGuid();
        var firstSpace = await _fixture.SeedSpaceAsync();
        var secondSpace = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(firstSpace.SpaceId, member, "Member");
        var route = $"/api/v1/spaces/{firstSpace.SpaceId}/integrity";

        using var unauthenticated = await _client!.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        var unauthenticatedProblem = await unauthenticated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("auth.unauthenticated", unauthenticatedProblem.GetProperty("code").GetString());

        using var nonMember = await GetWithoutExpectedStatusAsync(route, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, nonMember.StatusCode);
        var nonMemberProblem = await nonMember.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("auth.not_a_member", nonMemberProblem.GetProperty("code").GetString());

        using var crossSpace = await GetWithoutExpectedStatusAsync(
            $"/api/v1/spaces/{secondSpace.SpaceId}/integrity", member);
        Assert.Equal(HttpStatusCode.Forbidden, crossSpace.StatusCode);
        var crossSpaceProblem = await crossSpace.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("auth.not_a_member", crossSpaceProblem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Report_views_are_fail_closed_without_a_space_context()
    {
        var space = await _fixture.SeedSpaceAsync();
        await SeedReportLedgerAsync(space.SpaceId);

        await using var connection = await _fixture.OpenAppNoContextAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM trial_balance; SELECT count(*) FROM balance_sheet_lines; SELECT count(*) FROM income_statement_lines;",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt64(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt64(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt64(0));
    }

    [Fact]
    public async Task Integrity_reports_unbalanced_state_without_skipping_hash_computation()
    {
        var member = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, member, "Member");
        var accounts = await SeedReportLedgerAsync(space.SpaceId);

        await using (var connection = await _fixture.OpenSuperuserAsync())
        await using (var command = new NpgsqlCommand(
            "ALTER TABLE journal_lines DISABLE TRIGGER trg_journal_lines_balanced; " +
            "INSERT INTO journal_lines (id, entry_id, space_id, account_id, amount_minor, currency, base_amount_minor) " +
            "VALUES (@line, @entry, @space, @account, 1, 'CHF', 1); " +
            "ALTER TABLE journal_lines ENABLE TRIGGER trg_journal_lines_balanced;",
            connection))
        {
            command.Parameters.AddWithValue("line", Guid.NewGuid());
            command.Parameters.AddWithValue("entry", accounts.EntryId);
            command.Parameters.AddWithValue("space", space.SpaceId);
            command.Parameters.AddWithValue("account", accounts.IncomeId);
            await command.ExecuteNonQueryAsync();
        }

        using var response = await GetAsync($"/api/v1/spaces/{space.SpaceId}/integrity", member);
        var integrity = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(integrity.GetProperty("balanced").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(integrity.GetProperty("trialBalanceHash").GetString()));
    }

    private async Task<ReportAccounts> SeedReportLedgerAsync(Guid spaceId)
    {
        var groupId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var liabilityId = Guid.NewGuid();
        var equityId = Guid.NewGuid();
        var incomeId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO account_groups (id, space_id, code_range, name, created_at) VALUES (@group, @space, int4range(2000, 2100), 'Report accounts', now()); " +
            "INSERT INTO accounts (id, space_id, group_id, code, name, currency, kind, is_active, created_at) VALUES " +
            "(@asset, @space, @group, 2000, 'Asset', 'CHF', 'asset', true, now()), " +
            "(@liability, @space, @group, 2001, 'Liability', 'CHF', 'liability', true, now()), " +
            "(@equity, @space, @group, 2002, 'Equity', 'CHF', 'equity', true, now()), " +
            "(@income, @space, @group, 2003, 'Income', 'CHF', 'income', true, now()), " +
            "(@expense, @space, @group, 2004, 'Expense', 'CHF', 'expense', true, now()); " +
            "INSERT INTO journal_entries (id, space_id, entry_no, date, status, description, created_by, created_at) VALUES (@entry, @space, 1, DATE '2026-06-30', 'posted', 'Report fixture', @actor, now()); " +
            "INSERT INTO journal_lines (id, entry_id, space_id, account_id, amount_minor, currency, base_amount_minor) VALUES " +
            "(@line1, @entry, @space, @asset, 100, 'CHF', 100), (@line2, @entry, @space, @liability, -40, 'CHF', -40), " +
            "(@line3, @entry, @space, @equity, -10, 'CHF', -10), (@line4, @entry, @space, @income, -80, 'CHF', -80), " +
            "(@line5, @entry, @space, @expense, 30, 'CHF', 30);",
            connection);
        command.Parameters.AddWithValue("group", groupId);
        command.Parameters.AddWithValue("space", spaceId);
        command.Parameters.AddWithValue("asset", assetId);
        command.Parameters.AddWithValue("liability", liabilityId);
        command.Parameters.AddWithValue("equity", equityId);
        command.Parameters.AddWithValue("income", incomeId);
        command.Parameters.AddWithValue("expense", expenseId);
        command.Parameters.AddWithValue("entry", entryId);
        command.Parameters.AddWithValue("actor", Guid.NewGuid());
        command.Parameters.AddWithValue("line1", Guid.NewGuid());
        command.Parameters.AddWithValue("line2", Guid.NewGuid());
        command.Parameters.AddWithValue("line3", Guid.NewGuid());
        command.Parameters.AddWithValue("line4", Guid.NewGuid());
        command.Parameters.AddWithValue("line5", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
        await using var refresh = new NpgsqlCommand("SELECT refresh_trial_balance_mat();", connection);
        await refresh.ExecuteNonQueryAsync();

        return new ReportAccounts(incomeId, entryId);
    }

    private async Task<HttpResponseMessage> GetWithoutExpectedStatusAsync(string route, Guid subject)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Add("X-Test-Subject", subject.ToString());
        request.Headers.Add("X-Test-Scope", "ledger.write");
        return await _client!.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetAsync(string route, Guid subject)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Add("X-Test-Subject", subject.ToString());
        request.Headers.Add("X-Test-Scope", "ledger.write");
        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private sealed record ReportAccounts(Guid IncomeId, Guid EntryId);
}