using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeafLedger.Host.Authorization;
using LeafLedger.IntegrationTests.Authorization;
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
public sealed class LedgerAuthorizationTests : IAsyncLifetime
{
    private readonly LedgerDbFixture _fixture;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public LedgerAuthorizationTests(LedgerDbFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _factory = CreateFactory();
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
    public async Task Unauthenticated_post_returns_401_problem_details()
    {
        var space = await SeedSpaceAsync();

        using var response = await SendPostAsync(space, Guid.Empty);

        var problem = await ReadProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal("auth.unauthenticated", problem.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Missing_scope_returns_403_before_membership_resolution()
    {
        var subject = Guid.NewGuid();
        var space = await SeedSpaceAsync(subject, "Member");

        using var response = await SendPostAsync(space, subject, scope: "other.scope");

        var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("auth.forbidden", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Authenticated_non_member_returns_403_not_a_member()
    {
        var space = await SeedSpaceAsync();

        using var response = await SendPostAsync(space, Guid.NewGuid());

        var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("auth.not_a_member", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Missing_tenant_returns_identity_unresolved_without_provisioning()
    {
        var subject = Guid.NewGuid();
        var space = await SeedSpaceAsync();
        using var request = CreateRequest(HttpMethod.Post, Route(space.SpaceId), space.AccountId, subject);
        request.Headers.Add("X-Test-Omit-Tenant", "true");

        using var response = await _client!.SendAsync(request);

        var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("auth.identity_unresolved", problem.GetProperty("code").GetString());
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM identity_links WHERE subject = @subject;",
            connection);
        command.Parameters.AddWithValue("subject", subject);
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Membership_under_raw_subject_does_not_bypass_identity_link_resolution()
    {
        var subject = Guid.NewGuid();
        var space = await SeedSpaceAsync();
        await using (var connection = await _fixture.OpenSuperuserAsync())
        await using (var command = new NpgsqlCommand(
            "INSERT INTO memberships (id, space_id, user_id, role, created_at) " +
            "VALUES (@id, @space, @user, 'Member', now());",
            connection))
        {
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("space", space.SpaceId);
            command.Parameters.AddWithValue("user", subject);
            await command.ExecuteNonQueryAsync();
        }

        using var response = await SendPostAsync(space, subject);

        var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("auth.not_a_member", problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Member")]
    [InlineData("Admin")]
    [InlineData("Owner")]
    public async Task Posting_roles_are_server_enforced(string role)
    {
        var subject = Guid.NewGuid();
        var space = await SeedSpaceAsync(subject, role);

        using var response = await SendPostAsync(space, subject);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_and_unknown_roles_are_denied()
    {
        var viewer = Guid.NewGuid();
        var viewerSpace = await SeedSpaceAsync(viewer, "Viewer");
        using var viewerResponse = await SendPostAsync(viewerSpace, viewer);
        var viewerProblem = await ReadProblemAsync(viewerResponse, HttpStatusCode.Forbidden);
        Assert.Equal("auth.permission_denied", viewerProblem.GetProperty("code").GetString());

        var unknown = Guid.NewGuid();
        var unknownSpace = await SeedSpaceAsync(unknown, "future-role");
        using var unknownResponse = await SendPostAsync(unknownSpace, unknown);
        var unknownProblem = await ReadProblemAsync(unknownResponse, HttpStatusCode.Forbidden);
        Assert.Equal("auth.permission_denied", unknownProblem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Member")]
    [InlineData("Admin")]
    [InlineData("Owner")]
    public async Task Reversal_roles_are_server_enforced(string role)
    {
        var subject = Guid.NewGuid();
        var space = await SeedSpaceAsync(subject, role);
        using var post = await SendPostAsync(space, subject);
        var posted = await post.Content.ReadFromJsonAsync<JsonElement>();
        var entryId = posted.GetProperty("id").GetGuid();

        using var reversal = await SendReverseAsync(space, entryId, subject);

        Assert.Equal(HttpStatusCode.Created, reversal.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_reverse_an_entry_posted_by_an_owner()
    {
        var owner = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var space = await SeedSpaceAsync(owner, "Owner");
        await _fixture.SeedMembershipAsync(space.SpaceId, viewer, "Viewer");
        using var post = await SendPostAsync(space, owner);
        var posted = await post.Content.ReadFromJsonAsync<JsonElement>();
        var entryId = posted.GetProperty("id").GetGuid();

        using var reversal = await SendReverseAsync(space, entryId, viewer);

        var problem = await ReadProblemAsync(reversal, HttpStatusCode.Forbidden);
        Assert.Equal("auth.permission_denied", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task License_denial_returns_403_before_membership_lookup()
    {
        await using var factory = CreateFactory(denyLicense: true);
        using var client = factory.CreateClient();
        var subject = Guid.NewGuid();
        var space = await SeedSpaceAsync();

        using var request = CreateRequest(HttpMethod.Post, Route(space.SpaceId), space.AccountId, subject);
        using var response = await client.SendAsync(request);

        var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("auth.license_inactive", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Persisted_actor_is_the_authenticated_subject_not_the_legacy_header()
    {
        var subject = Guid.NewGuid();
        var spoofedActor = Guid.NewGuid();
        var space = await SeedSpaceAsync(subject, "Member");

        using var request = CreateRequest(HttpMethod.Post, Route(space.SpaceId), space.AccountId, subject);
        request.Headers.Add("X-Actor-Id", spoofedActor.ToString());
        using var response = await _client!.SendAsync(request);
        var posted = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entryId = posted.GetProperty("id").GetGuid();

        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "SELECT created_by FROM journal_entries WHERE id = @id;",
            connection);
        command.Parameters.AddWithValue("id", entryId);
        var createdBy = (Guid)(await command.ExecuteScalarAsync())!;
        var internalUserId = await _fixture.ResolveIdentityLinkAsync(subject, Guid.Parse(TestAuthHandler.DefaultTenantId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(internalUserId, createdBy);
        Assert.NotEqual(spoofedActor, createdBy);
    }

    private WebApplicationFactory<Program> CreateFactory(bool denyLicense = false)
    {
        return new WebApplicationFactory<Program>()
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
                    if (denyLicense)
                    {
                        services.RemoveAll<ILicenseEntitlement>();
                        services.AddScoped<ILicenseEntitlement, DenyLicenseEntitlement>();
                    }
                });
            });
    }

    private async Task<SeededSpace> SeedSpaceAsync(Guid? userId = null, string role = "Member")
    {
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedPeriodAsync(space.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        if (userId is Guid subjectId)
        {
            await _fixture.SeedMembershipAsync(space.SpaceId, subjectId, role);
        }

        return space;
    }

    private async Task<HttpResponseMessage> SendPostAsync(SeededSpace space, Guid subject, string scope = "ledger.write")
    {
        using var request = CreateRequest(HttpMethod.Post, Route(space.SpaceId), space.AccountId, subject, scope);
        return await _client!.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendReverseAsync(SeededSpace space, Guid entryId, Guid subject)
    {
        using var request = CreateRequest(HttpMethod.Post, Route(space.SpaceId, $"/{entryId}/reverse"), space.AccountId, subject);
        request.Content = JsonContent.Create(new { date = "2026-07-01" });
        return await _client!.SendAsync(request);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string route, Guid accountId, Guid subject, string? scope = "ledger.write")
    {
        var request = new HttpRequestMessage(method, route)
        {
            Content = JsonContent.Create(new
            {
                date = "2026-06-30",
                description = "Authorization test",
                lines = new[]
                {
                    new { accountId, amountMinor = 100L, currency = "CHF", baseAmountMinor = 100L },
                    new { accountId, amountMinor = -100L, currency = "CHF", baseAmountMinor = -100L },
                },
            }),
        };
        if (subject != Guid.Empty)
        {
            request.Headers.Add("X-Test-Subject", subject.ToString());
        }

        if (scope is not null)
        {
            request.Headers.Add("X-Test-Scope", scope);
        }

        if (method == HttpMethod.Post)
        {
            request.Headers.Add("Idempotency-Key", Ulid.NewUlid().ToString());
        }

        return request;
    }

    private static string Route(Guid spaceId, string suffix = "") =>
        $"/api/v1/spaces/{spaceId}/journal-entries{suffix}";

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private sealed class DenyLicenseEntitlement : ILicenseEntitlement
    {
        public Task<bool> IsEntitledAsync(Guid subjectId, Guid spaceId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}