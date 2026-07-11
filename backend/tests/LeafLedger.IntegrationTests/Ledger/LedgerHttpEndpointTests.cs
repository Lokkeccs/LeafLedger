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
public sealed class LedgerHttpEndpointTests : IAsyncLifetime
{
    private readonly LedgerDbFixture _fixture;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public LedgerHttpEndpointTests(LedgerDbFixture fixture) => _fixture = fixture;

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
    public async Task Post_returns_201_location_and_contract_body()
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceWithOpenPeriodAsync(actor);

        using var response = await SendJsonAsync(
            HttpMethod.Post, Route(space.SpaceId), new
            {
                date = "2026-06-30",
                description = "HTTP posting",
                lines = Lines(space.AccountId, 100, -100),
            }, actor);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.NotNull(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
        Assert.Equal(1, body.GetProperty("entryNo").GetInt64());
    }

    [Fact]
    public async Task Unbalanced_post_returns_422_problem_details_with_stable_code_and_issues()
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceWithOpenPeriodAsync(actor);

        using var response = await SendJsonAsync(
            HttpMethod.Post, Route(space.SpaceId), new
            {
                date = "2026-06-30",
                description = "HTTP imbalance",
                lines = Lines(space.AccountId, 100, -90),
            }, actor);

        var problem = await ReadProblemAsync(response, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("journal_entry.unbalanced", problem.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal("journal_entry.unbalanced", problem.GetProperty("issues")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reversal_returns_201_with_link_and_missing_target_returns_404_problem_details()
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceWithOpenPeriodAsync(actor);
        using var post = await SendJsonAsync(
            HttpMethod.Post, Route(space.SpaceId), new
            {
                date = "2026-06-30",
                description = "HTTP reversal source",
                lines = Lines(space.AccountId, 100, -100),
            }, actor);
        var posted = await post.Content.ReadFromJsonAsync<JsonElement>();
        var entryId = posted.GetProperty("id").GetGuid();

        using var reversal = await SendJsonAsync(
            HttpMethod.Post, Route(space.SpaceId, $"/{entryId}/reverse"),
            new { date = "2026-07-01" }, actor);

        Assert.Equal(HttpStatusCode.Created, reversal.StatusCode);
        var reversalBody = await reversal.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(entryId, reversalBody.GetProperty("reversesEntryId").GetGuid());

        using var missing = await SendJsonAsync(
            HttpMethod.Post, Route(space.SpaceId, $"/{Guid.NewGuid()}/reverse"),
            new { date = "2026-07-01" }, actor);
        var missingProblem = await ReadProblemAsync(missing, HttpStatusCode.NotFound);
        Assert.Equal("journal_entry.not_found", missingProblem.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Missing_actor_returns_401_and_spoofed_header_is_ignored()
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceWithOpenPeriodAsync(actor);
        var payload = new
        {
            date = "2026-06-30",
            description = "HTTP actor validation",
            lines = Lines(space.AccountId, 100, -100),
        };

        using var missing = await SendJsonAsync(HttpMethod.Post, Route(space.SpaceId), payload);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);

        using var spoofed = await SendJsonAsync(HttpMethod.Post, Route(space.SpaceId), payload, actor, "not-a-guid");
        Assert.Equal(HttpStatusCode.Created, spoofed.StatusCode);

        using var wrongRoute = await _client!.GetAsync(Route(space.SpaceId));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongRoute.StatusCode);
    }

    [Fact]
    public async Task Reversal_of_extreme_amount_returns_structured_422_instead_of_overflow()
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceWithOpenPeriodAsync(actor);
        var entryId = Guid.NewGuid();
        var firstLineId = Guid.NewGuid();
        var secondLineId = Guid.NewGuid();
        var thirdLineId = Guid.NewGuid();

        await using (var connection = await _fixture.OpenSuperuserAsync())
        await using (var command = new NpgsqlCommand(
            "INSERT INTO journal_entries (id, space_id, entry_no, date, status, description, created_by, created_at) " +
            "VALUES (@entry, @space, 1, DATE '2026-06-30', 'posted', 'Extreme source', @actor, now()); " +
            "INSERT INTO journal_lines (id, entry_id, space_id, account_id, amount_minor, currency, base_amount_minor) " +
            "VALUES (@line1, @entry, @space, @account, @min, 'CHF', @min), " +
            "(@line2, @entry, @space, @account, @max, 'CHF', @max), " +
            "(@line3, @entry, @space, @account, 1, 'CHF', 1);", connection))
        {
            command.Parameters.AddWithValue("entry", entryId);
            command.Parameters.AddWithValue("space", space.SpaceId);
            command.Parameters.AddWithValue("actor", Guid.NewGuid());
            command.Parameters.AddWithValue("account", space.AccountId);
            command.Parameters.AddWithValue("line1", firstLineId);
            command.Parameters.AddWithValue("line2", secondLineId);
            command.Parameters.AddWithValue("line3", thirdLineId);
            command.Parameters.AddWithValue("min", long.MinValue);
            command.Parameters.AddWithValue("max", long.MaxValue);
            await command.ExecuteNonQueryAsync();
        }

        using var response = await SendJsonAsync(
            HttpMethod.Post,
            Route(space.SpaceId, $"/{entryId}/reverse"),
            new { date = "2026-07-01" },
            actor);

        var problem = await ReadProblemAsync(response, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("journal_entry.amount_out_of_range", problem.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal("journal_entry.amount_out_of_range", problem.GetProperty("issues")[0].GetProperty("code").GetString());
    }

    private async Task<SeededSpace> SeedSpaceWithOpenPeriodAsync(Guid userId)
    {
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedPeriodAsync(space.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        await _fixture.SeedMembershipAsync(space.SpaceId, userId);
        return space;
    }

    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method,
        string route,
        object payload,
        Guid? subject = null,
        string? spoofedActor = null,
        string? scope = "ledger.write")
    {
        using var request = new HttpRequestMessage(method, route)
        {
            Content = JsonContent.Create(payload),
        };
        if (subject is not null)
        {
            request.Headers.Add("X-Test-Subject", subject.ToString());
        }
        if (scope is not null)
        {
            request.Headers.Add("X-Test-Scope", scope);
        }
        if (spoofedActor is not null)
        {
            request.Headers.Add("X-Actor-Id", spoofedActor);
        }

        return await _client!.SendAsync(request);
    }

    private static string Route(Guid spaceId, string suffix = "") =>
        $"/api/v1/spaces/{spaceId}/journal-entries{suffix}";

    private static object[] Lines(Guid accountId, long first, long second) =>
    [
        new { accountId, amountMinor = first, currency = "CHF", baseAmountMinor = first },
        new { accountId, amountMinor = second, currency = "CHF", baseAmountMinor = second },
    ];

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}