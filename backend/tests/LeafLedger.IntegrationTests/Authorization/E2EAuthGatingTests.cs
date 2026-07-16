using System.Net;
using LeafLedger.Host.Authorization;
using LeafLedger.IntegrationTests.Ledger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LeafLedger.IntegrationTests.Authorization;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class E2EAuthGatingTests
{
    private static readonly Guid MemberASubject = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MemberATenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly LedgerDbFixture _fixture;

    public E2EAuthGatingTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task E2E_scheme_is_absent_outside_development_even_when_flag_is_enabled()
    {
        await using var factory = CreateFactory("Production", enabled: true, connectionString: string.Empty);
        var schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.Null(await schemes.GetSchemeAsync(E2ETestAuthenticationHandler.AuthenticationScheme));
    }

    [Fact]
    public async Task E2E_scheme_is_absent_in_development_when_flag_is_disabled()
    {
        await using var factory = CreateFactory("Development", enabled: false, connectionString: string.Empty);
        var schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.Null(await schemes.GetSchemeAsync(E2ETestAuthenticationHandler.AuthenticationScheme));
    }

    [Fact]
    public async Task Enabled_scheme_authenticates_seeded_member_through_real_membership_path()
    {
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, MemberASubject, "Owner", MemberATenant);
        await using var factory = CreateFactory("Development", enabled: true, _fixture.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/spaces/{space.SpaceId}/accounts");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "e2e:a");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Enabled_scheme_rejects_unknown_member_token()
    {
        await using var factory = CreateFactory("Development", enabled: true, _fixture.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/spaces/{Guid.NewGuid()}/accounts");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "e2e:unknown");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        bool enabled,
        string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestServer();
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
            builder.UseSetting("Authentication:E2E:Enabled", enabled.ToString());
            builder.UseSetting("Seed:Enabled", "false");
        });
}
