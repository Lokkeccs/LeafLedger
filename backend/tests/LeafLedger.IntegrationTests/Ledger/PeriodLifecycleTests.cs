using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeafLedger.Modules.Ledger.Application.Periods;
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
public sealed class PeriodLifecycleTests : IAsyncLifetime
{
    private readonly LedgerDbFixture _fixture;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public PeriodLifecycleTests(LedgerDbFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestServer();
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Postgres", _fixture.ConnectionString);
            builder.ConfigureTestServices(services => services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.AuthenticationScheme, _ => { }));
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
    public async Task Create_replay_transition_and_lock_are_atomic()
    {
        var actor = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(space, actor, "Owner");
        var payload = new { name = "FY 2026", startDate = "2026-01-01", endExclusive = "2027-01-01" };
        var key = Ulid.NewUlid().ToString();

        using var first = await SendAsync(HttpMethod.Post, Periods(space), payload, actor, key);
        using var replay = await SendAsync(HttpMethod.Post, Periods(space), payload, actor, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var periodId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var close = await SendAsync(HttpMethod.Post, Periods(space, $"/{periodId}/close"), new { }, actor);
        using var reopen = await SendAsync(HttpMethod.Post, Periods(space, $"/{periodId}/reopen"), new { }, actor);
        using var lockResponse = await SendAsync(HttpMethod.Post, Periods(space, $"/{periodId}/lock"), new { }, actor);
        using var invalid = await SendAsync(HttpMethod.Post, Periods(space, $"/{periodId}/reopen"), new { }, actor);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reopen.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal("period.locked", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Overlap_and_invalid_range_are_rejected()
    {
        var actor = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(space, actor, "Admin");
        using var first = await SendAsync(HttpMethod.Post, Periods(space), new { name = "First", startDate = "2026-01-01", endExclusive = "2026-07-01" }, actor);
        using var overlap = await SendAsync(HttpMethod.Post, Periods(space), new { name = "Overlap", startDate = "2026-06-01", endExclusive = "2026-08-01" }, actor);
        using var invalid = await SendAsync(HttpMethod.Post, Periods(space), new { name = "Empty", startDate = "2026-09-01", endExclusive = "2026-09-01" }, actor);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        Assert.Equal("period.overlap", (await overlap.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
    }

    [Fact]
    public async Task List_is_authorized_and_database_exclusion_is_second_wall()
    {
        var actor = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(space, actor, "Owner");
        await _fixture.SeedPeriodAsync(space, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        using var list = await SendAsync(HttpMethod.Get, Periods(space), null, actor, idempotencyKey: null);
        var periods = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(1, periods.GetArrayLength());

        await using var connection = await _fixture.OpenAppAsync(space, actor.ToString());
        await using var command = new NpgsqlCommand("INSERT INTO periods (id, space_id, name, start_date, end_exclusive, state, created_at) VALUES (@id, @space, 'Overlap SQL', '2026-06-01', '2026-08-01', 'open', now());", connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("space", space);
        await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Bootstrap_is_idempotent_for_the_same_fiscal_year()
    {
        var actor = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(space, actor, "Owner");

        PeriodOutcome first;
        await using (var firstScope = _factory!.Services.CreateAsyncScope())
        {
            first = await firstScope.ServiceProvider.GetRequiredService<IPeriodLifecycleService>()
                .BootstrapOpenPeriodAsync(space, actor, new DateOnly(2026, 1, 1), Ulid.NewUlid().ToString());
        }

        PeriodOutcome second;
        IReadOnlyList<PeriodResponse> periods;
        await using (var secondScope = _factory.Services.CreateAsyncScope())
        {
            var service = secondScope.ServiceProvider.GetRequiredService<IPeriodLifecycleService>();
            second = await service.BootstrapOpenPeriodAsync(space, actor, new DateOnly(2026, 1, 1), Ulid.NewUlid().ToString());
        }

        await using (var listScope = _factory.Services.CreateAsyncScope())
        {
            periods = await listScope.ServiceProvider.GetRequiredService<IPeriodLifecycleService>()
                .ListAsync(space, actor);
        }

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Equal(new DateOnly(2026, 1, 1), second.Value.StartDate);
        Assert.Equal(new DateOnly(2027, 1, 1), second.Value.EndExclusive);
        Assert.Single(periods);
    }

    [Fact]
    public async Task Posting_accepts_last_inclusive_day_and_rejects_end_exclusive()
    {
        var actor = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, actor, "Owner");
        var periodId = await CreatePeriodAsync(space.SpaceId, actor, "2026-01-01", "2026-07-01");

        using var lastDay = await PostJournalAsync(space, actor, "2026-06-30");
        using var endExclusive = await PostJournalAsync(space, actor, "2026-07-01");

        Assert.Equal(HttpStatusCode.Created, lastDay.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, endExclusive.StatusCode);
        Assert.Equal("posting_period.not_defined", (await endExclusive.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.NotEqual(Guid.Empty, periodId);
    }

    [Fact]
    public async Task Posting_in_a_gap_is_rejected_as_not_defined()
    {
        var actor = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, actor, "Admin");
        await CreatePeriodAsync(space.SpaceId, actor, "2026-01-01", "2026-06-01");

        using var response = await PostJournalAsync(space, actor, "2026-06-15");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("posting_period.not_defined", (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Bootstrap_period_allows_an_in_range_posting()
    {
        var actor = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, actor, "Owner");

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var outcome = await scope.ServiceProvider.GetRequiredService<IPeriodLifecycleService>()
                .BootstrapOpenPeriodAsync(space.SpaceId, actor, new DateOnly(2026, 1, 1), Ulid.NewUlid().ToString());
            Assert.True(outcome.IsSuccess);
        }

        using var response = await PostJournalAsync(space, actor, "2026-06-30");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("close", "posting_period.not_open")]
    [InlineData("lock", "posting_period.not_open")]
    public async Task Posting_in_a_closed_or_locked_period_is_rejected(string transition, string expectedCode)
    {
        var actor = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, actor, "Owner");
        var periodId = await CreatePeriodAsync(space.SpaceId, actor, "2026-01-01", "2027-01-01");

        using var transitionResponse = await SendAsync(HttpMethod.Post,
            Periods(space.SpaceId, $"/{periodId}/{transition}"), new { }, actor);
        using var post = await PostJournalAsync(space, actor, "2026-06-30");

        Assert.Equal(HttpStatusCode.OK, transitionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, post.StatusCode);
        Assert.Equal(expectedCode, (await post.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("errors")[0].GetProperty("code").GetString());
    }

    private async Task<Guid> CreatePeriodAsync(Guid spaceId, Guid actor, string startDate, string endExclusive)
    {
        using var response = await SendAsync(HttpMethod.Post, Periods(spaceId), new
        {
            name = "Test period",
            startDate,
            endExclusive,
        }, actor);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> PostJournalAsync(SeededSpace space, Guid actor, string date)
    {
        return await SendAsync(HttpMethod.Post, $"/api/v1/spaces/{space.SpaceId}/journal-entries", new
        {
            date,
            description = "Period boundary test",
            lines = new[]
            {
                new { accountId = space.AccountId, amountMinor = 100, currency = "CHF", baseAmountMinor = 100 },
                new { accountId = space.AccountId, amountMinor = -100, currency = "CHF", baseAmountMinor = -100 },
            },
        }, actor);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, object? payload, Guid actor, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, route);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        request.Headers.Add("X-Test-Subject", actor.ToString());
        request.Headers.Add("X-Test-Scope", "ledger.write");
        if (method == HttpMethod.Post)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey ?? Ulid.NewUlid().ToString());
        }

        return await _client!.SendAsync(request);
    }

    private static string Periods(Guid spaceId, string suffix = "") => $"/api/v1/spaces/{spaceId}/periods{suffix}";
}
