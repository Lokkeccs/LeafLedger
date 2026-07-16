using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using LeafLedger.IntegrationTests.Ledger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace LeafLedger.IntegrationTests.Authorization;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class EntraTokenValidationTests : IAsyncLifetime
{
    private const string TenantId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0";
    private const string Audience = "api://leafledger";

    private readonly LedgerDbFixture _fixture;
    private readonly RSA _trustedKey = RSA.Create(2048);
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public EntraTokenValidationTests(LedgerDbFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _factory = CreateFactory();
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    private WebApplicationFactory<Program> CreateFactory(IEnumerable<string>? tenantAllowlist = null)
    {
        var signingKey = new RsaSecurityKey(_trustedKey);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestServer();
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Postgres", _fixture.ConnectionString);
            builder.UseSetting("Authentication:Audiences:0", Audience);
            var tenants = tenantAllowlist?.ToArray() ?? [];
            for (var index = 0; index < tenants.Length; index++)
            {
                builder.UseSetting($"Authentication:TenantAllowlist:{index}", tenants[index]);
            }

            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.ConfigurationManager = null;
                    options.TokenValidationParameters.IssuerSigningKey = signingKey;
                    options.TokenValidationParameters.IssuerSigningKeys = [signingKey];
                });
            });
        });
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        _trustedKey.Dispose();
    }

    [Fact]
    public async Task Validly_signed_member_token_reaches_wp06_authorization()
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceAsync(actor);

        using var response = await PostAsync(space, CreateToken(actor));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Valid_v1_issuer_reaches_wp06_authorization()
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceAsync(actor);

        using var response = await PostAsync(space, CreateToken(actor,
            issuer: $"https://sts.windows.net/{TenantId}/"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_signature_is_rejected_before_authorization()
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceAsync(actor);
        using var otherKey = RSA.Create(2048);

        using var response = await PostAsync(space, CreateToken(actor, signingKey: otherKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unsigned_token_is_rejected()
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceAsync(actor);

        using var response = await PostAsync(space, CreateToken(actor, unsigned: true));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong-audience", null, null)]
    [InlineData(null, "https://issuer.example.test/tenant/v2.0", null)]
    [InlineData(null, Issuer, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")]
    [InlineData(null, Issuer + "?unexpected=1", null)]
    [InlineData(null, Issuer + "#unexpected", null)]
    public async Task Invalid_audience_or_issuer_claims_are_rejected(string? audience, string? issuer, string? tenantId)
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceAsync(actor);

        using var response = await PostAsync(space, CreateToken(
            actor,
            audience: audience ?? Audience,
            issuer: issuer ?? Issuer,
            tenantId: tenantId ?? TenantId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Missing_issuer_is_rejected()
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceAsync(actor);

        using var response = await PostAsync(space, CreateToken(actor, issuer: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Configured_tenant_allowlist_rejects_unlisted_tenants()
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceAsync(actor);
        await using var factory = CreateFactory(["bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"]);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, space, CreateToken(actor));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Configured_tenant_allowlist_accepts_listed_tenants()
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceAsync(actor);
        await using var factory = CreateFactory([TenantId]);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, space, CreateToken(actor));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var actor = Guid.NewGuid();
        var space = await SeedSpaceAsync(actor);

        using var response = await PostAsync(space, CreateToken(actor, expires: DateTime.UtcNow.AddMinutes(-5)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<SeededSpace> SeedSpaceAsync(Guid actor)
    {
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedPeriodAsync(space.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        await _fixture.SeedMembershipAsync(space.SpaceId, actor, "Member", Guid.Parse(TenantId));
        return space;
    }

    private Task<HttpResponseMessage> PostAsync(SeededSpace space, string token) =>
        PostAsync(_client!, space, token);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, SeededSpace space, string token)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/spaces/{space.SpaceId}/journal-entries")
        {
            Content = JsonContent.Create(new
            {
                date = "2026-06-30",
                description = "Entra validation test",
                lines = new[]
                {
                    new { accountId = space.AccountId, amountMinor = 100L, currency = "CHF", baseAmountMinor = 100L },
                    new { accountId = space.AccountId, amountMinor = -100L, currency = "CHF", baseAmountMinor = -100L },
                },
            }),
        };
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Add("Idempotency-Key", Ulid.NewUlid().ToString());
        return await client.SendAsync(request);
    }

    private string CreateToken(
        Guid actor,
        RSA? signingKey = null,
        string audience = Audience,
        string? issuer = Issuer,
        string tenantId = TenantId,
        DateTime? expires = null,
        bool unsigned = false)
    {
        var handler = new JwtSecurityTokenHandler();
        var tokenExpires = expires ?? DateTime.UtcNow.AddMinutes(5);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim("oid", actor.ToString()),
                new Claim("sub", actor.ToString()),
                new Claim("tid", tenantId),
                new Claim("scope", "ledger.write"),
            ]),
            Audience = audience,
            NotBefore = tokenExpires.AddMinutes(-10),
            Expires = tokenExpires,
        };

        if (issuer is not null)
        {
            descriptor.Issuer = issuer;
        }

        if (!unsigned)
        {
            descriptor.SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(signingKey ?? _trustedKey),
                SecurityAlgorithms.RsaSha256);
        }

        return handler.CreateEncodedJwt(descriptor);
    }
}