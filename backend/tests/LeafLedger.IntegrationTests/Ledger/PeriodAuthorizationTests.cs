using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeafLedger.IntegrationTests.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class PeriodAuthorizationTests : IAsyncLifetime
{
    private readonly LedgerDbFixture _fixture;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public PeriodAuthorizationTests(LedgerDbFixture fixture) => _fixture = fixture;

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

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    public async Task Owner_and_admin_can_create_and_list_periods(string role)
    {
        var actor = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(space, actor, role);

        using var create = await SendAsync(HttpMethod.Post, Periods(space), actor, new
        {
            name = "FY 2026",
            startDate = "2026-01-01",
            endExclusive = "2027-01-01",
        });
        using var list = await SendAsync(HttpMethod.Get, Periods(space), actor);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(1, (await list.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
    }

    [Theory]
    [InlineData("Member")]
    [InlineData("Viewer")]
    public async Task Member_and_viewer_cannot_manage_periods(string role)
    {
        var actor = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(space, actor, role);

        using var response = await SendAsync(HttpMethod.Post, Periods(space), actor, new
        {
            name = "FY 2026",
            startDate = "2026-01-01",
            endExclusive = "2027-01-01",
        });

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("auth.permission_denied", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unauthenticated_period_request_returns_401()
    {
        var space = await _fixture.SeedBareSpaceAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, Periods(space));
        using var response = await _client!.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.unauthenticated", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_member_of_another_space_cannot_list_or_create_periods()
    {
        var actor = Guid.NewGuid();
        var ownedSpace = await _fixture.SeedBareSpaceAsync();
        var otherSpace = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(ownedSpace, actor, "Owner");

        using var list = await SendAsync(HttpMethod.Get, Periods(otherSpace), actor);
        using var create = await SendAsync(HttpMethod.Post, Periods(otherSpace), actor, new
        {
            name = "Cross-space",
            startDate = "2026-01-01",
            endExclusive = "2027-01-01",
        });

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, Guid actor, object? payload = null)
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
            request.Headers.Add("Idempotency-Key", Ulid.NewUlid().ToString());
        }

        return await _client!.SendAsync(request);
    }

    private static string Periods(Guid spaceId) => $"/api/v1/spaces/{spaceId}/periods";
}
