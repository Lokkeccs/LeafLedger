using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeafLedger.IntegrationTests.Authorization;
using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class ReportRefreshLatencyTests
{
    private readonly LedgerDbFixture _fixture;

    public ReportRefreshLatencyTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Posting_returns_with_refresh_still_pending_after_commit()
    {
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedPeriodAsync(space.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        var actor = Guid.NewGuid();
        await _fixture.SeedMembershipAsync(space.SpaceId, actor, "Owner");
        var secondAccountId = await SeedBaselineAsync(space);
        var queue = new CommitObservingQueue(_fixture.ConnectionString, space.SpaceId);

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseTestServer();
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:Postgres", _fixture.ConnectionString);
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IReportRefreshQueue>();
                    services.AddSingleton<IReportRefreshQueue>(queue);
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                        options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
                    }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.AuthenticationScheme, _ => { });
                });
            });
        using var client = factory.CreateClient();
        await queue.RefreshBlocked.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var baselineIntegrity = await GetAsync(client, $"/api/v1/spaces/{space.SpaceId}/integrity", actor);
        var baselineDashboard = await GetAsync(client, $"/api/v1/spaces/{space.SpaceId}/reports/trial-balance", actor);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/spaces/{space.SpaceId}/journal-entries")
        {
            Content = JsonContent.Create(new
            {
                date = new DateOnly(2026, 6, 30),
                description = "Post-commit refresh latency",
                lines = new[]
                {
                    new { accountId = space.AccountId, amountMinor = 25L, currency = "CHF", baseAmountMinor = 25L },
                    new { accountId = secondAccountId, amountMinor = -25L, currency = "CHF", baseAmountMinor = -25L },
                },
            }),
        };
        request.Headers.Add("X-Test-Subject", actor.ToString());
        request.Headers.Add("X-Test-Scope", "ledger.write");
        request.Headers.Add("Idempotency-Key", "00000000000000000000000000");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(queue.CommitWasVisibleBeforeEnqueue);
        Assert.True(queue.EnqueueObserved.Task.IsCompleted);

        var committedIntegrity = await GetAsync(client, $"/api/v1/spaces/{space.SpaceId}/integrity", actor);
        var staleDashboard = await GetAsync(client, $"/api/v1/spaces/{space.SpaceId}/reports/trial-balance", actor);
        Assert.NotEqual(baselineIntegrity, committedIntegrity);
        Assert.Equal(baselineDashboard, staleDashboard);

        await RefreshCoalescingService.RunRefreshPassAsync(_fixture.ConnectionString);
        var refreshedDashboard = await GetAsync(client, $"/api/v1/spaces/{space.SpaceId}/reports/trial-balance", actor);
        Assert.NotEqual(staleDashboard, refreshedDashboard);
    }

    private async Task<Guid> SeedBaselineAsync(SeededSpace space)
    {
        var secondAccountId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO accounts (id, space_id, group_id, code, name, currency, kind, is_active, created_at) " +
            "VALUES (@account, @space, @group, 1001, 'Baseline account', 'CHF', 'asset', true, now()); " +
            "INSERT INTO journal_entries (id, space_id, entry_no, date, status, description, created_by, created_at) " +
            "VALUES (@entry, @space, 1, DATE '2026-06-30', 'posted', 'Baseline fixture', @actor, now()); " +
            "INSERT INTO journal_lines (id, entry_id, space_id, account_id, amount_minor, currency, base_amount_minor) VALUES " +
            "(@line1, @entry, @space, @account, 100, 'CHF', 100), " +
            "(@line2, @entry, @space, @spaceAccount, -100, 'CHF', -100);",
            connection);
        command.Parameters.AddWithValue("account", secondAccountId);
        command.Parameters.AddWithValue("space", space.SpaceId);
        command.Parameters.AddWithValue("group", space.GroupId);
        command.Parameters.AddWithValue("entry", entryId);
        command.Parameters.AddWithValue("actor", Guid.NewGuid());
        command.Parameters.AddWithValue("line1", Guid.NewGuid());
        command.Parameters.AddWithValue("line2", Guid.NewGuid());
        command.Parameters.AddWithValue("spaceAccount", space.AccountId);
        await command.ExecuteNonQueryAsync();
        await using var refresh = new NpgsqlCommand("SELECT refresh_trial_balance_mat();", connection);
        await refresh.ExecuteScalarAsync();
        return secondAccountId;
    }

    private static async Task<string> GetAsync(HttpClient client, string route, Guid actor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Add("X-Test-Subject", actor.ToString());
        request.Headers.Add("X-Test-Scope", "ledger.write");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private sealed class CommitObservingQueue : IReportRefreshQueue
    {
        private readonly string _connectionString;
        private readonly Guid _spaceId;

        public CommitObservingQueue(string connectionString, Guid spaceId)
        {
            _connectionString = connectionString;
            _spaceId = spaceId;
        }

        public TaskCompletionSource<bool> EnqueueObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> RefreshBlocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CommitWasVisibleBeforeEnqueue { get; private set; }

        public bool TryEnqueue(Guid spaceId)
        {
            using var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            using var command = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM journal_entries WHERE space_id = @space AND description = 'Post-commit refresh latency');",
                connection);
            command.Parameters.AddWithValue("space", _spaceId);
            CommitWasVisibleBeforeEnqueue = (bool)command.ExecuteScalar()!;
            EnqueueObserved.TrySetResult(true);
            return true;
        }

        public async Task<ReportRefreshBatch> ReadBatchAsync(TimeSpan debounceWindow, CancellationToken cancellationToken)
        {
            RefreshBlocked.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new OperationCanceledException(cancellationToken);
        }
    }
}