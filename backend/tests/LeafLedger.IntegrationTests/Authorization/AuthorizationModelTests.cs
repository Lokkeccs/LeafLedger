using Xunit;

using System.Security.Claims;
using LeafLedger.Host.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace LeafLedger.IntegrationTests.Authorization;

public sealed class AuthorizationModelTests
{
    [Theory]
    [InlineData("Owner", SpaceRole.Owner)]
    [InlineData("owner", SpaceRole.Owner)]
    [InlineData("Admin", SpaceRole.Admin)]
    [InlineData("Member", SpaceRole.Member)]
    [InlineData("Viewer", SpaceRole.Viewer)]
    public void Parses_known_roles_case_insensitively(string value, SpaceRole expected)
    {
        Assert.True(SpaceRoleParser.TryParse(value, out var role));
        Assert.Equal(expected, role);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Unknown")]
    public void Rejects_unknown_or_blank_roles(string? value)
    {
        Assert.False(SpaceRoleParser.TryParse(value, out _));
    }

    [Theory]
    [InlineData(SpaceRole.Owner, ModulePermissions.PostLedger, true)]
    [InlineData(SpaceRole.Owner, ModulePermissions.ReverseLedger, true)]
    [InlineData(SpaceRole.Admin, ModulePermissions.PostLedger, true)]
    [InlineData(SpaceRole.Admin, ModulePermissions.ReverseLedger, true)]
    [InlineData(SpaceRole.Member, ModulePermissions.PostLedger, true)]
    [InlineData(SpaceRole.Member, ModulePermissions.ReverseLedger, true)]
    [InlineData(SpaceRole.Viewer, ModulePermissions.PostLedger, false)]
    [InlineData(SpaceRole.Viewer, ModulePermissions.ReverseLedger, false)]
    public void Maps_roles_to_server_side_module_permissions(SpaceRole role, string permission, bool expected)
    {
        Assert.Equal(expected, ModulePermissions.Allows(role, permission));
    }

    [Fact]
    public void Configures_signed_tokens_with_audience_and_issuer_allowlists()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Audiences:0"] = "api://leafledger",
                ["Authentication:ValidIssuers:0"] = "https://issuer.example.test/tenant/v2.0",
            })
            .Build();

        var parameters = AuthenticationConfiguration.CreateTokenValidationParameters(configuration);

        Assert.True(parameters.RequireSignedTokens);
        Assert.True(parameters.ValidateIssuer);
        Assert.Equal(["https://issuer.example.test/tenant/v2.0"], parameters.ValidIssuers);
        Assert.True(parameters.ValidateAudience);
        Assert.Equal(["api://leafledger"], parameters.ValidAudiences);
    }

    [Fact]
    public void Resolves_subject_and_space_delimited_scopes_from_the_authenticated_claims()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim("oid", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        new Claim("scope", "ledger.write profile"),
                    ],
                    "test")),
            },
        };

        var user = new HttpContextCurrentUser(accessor);

        Assert.True(user.IsAuthenticated);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), user.SubjectId);
        Assert.True(user.HasScope("ledger.write"));
        Assert.True(user.HasScope("profile"));
    }
}