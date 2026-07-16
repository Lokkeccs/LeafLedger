using Xunit;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using LeafLedger.Host.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
    public void Configures_signed_tokens_with_audience_and_issuer_validator()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Audiences:0"] = "api://leafledger",
            })
            .Build();

        var parameters = AuthenticationConfiguration.CreateTokenValidationParameters(configuration);

        Assert.True(parameters.RequireSignedTokens);
        Assert.True(parameters.ValidateIssuer);
        Assert.NotNull(parameters.IssuerValidator);
        Assert.True(parameters.ValidateAudience);
        Assert.Equal(["api://leafledger"], parameters.ValidAudiences);
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/v2.0", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", true)]
    [InlineData("https://sts.windows.net/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", true)]
    [InlineData("https://login.microsoftonline.com/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/v2.0", "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", false)]
    [InlineData("http://login.microsoftonline.com/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/v2.0", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", false)]
    [InlineData("https://issuer.example.test/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/v2.0", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", false)]
    [InlineData("https://login.microsoftonline.com/not-a-guid/v2.0", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", false)]
    public void Validates_entra_issuer_shape_and_tid_binding(string issuer, string tenantId, bool expected)
    {
        if (expected)
        {
            Assert.Equal(issuer, EntraIssuerValidator.Validate(issuer, tenantId));
        }
        else
        {
            Assert.Throws<SecurityTokenInvalidIssuerException>(() => EntraIssuerValidator.Validate(issuer, tenantId));
        }
    }

    [Fact]
    public void Applies_optional_tenant_allowlist()
    {
        const string issuer = "https://login.microsoftonline.com/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/v2.0";

        Assert.Equal(issuer, EntraIssuerValidator.Validate(issuer, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", ["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]));
        Assert.Throws<SecurityTokenInvalidIssuerException>(() =>
            EntraIssuerValidator.Validate(issuer, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", ["bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"]));
    }

    [Fact]
    public void Rejects_empty_audience_allowlist_outside_development()
    {
        var configuration = new ConfigurationBuilder().Build();
        var options = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions();

        Assert.Throws<InvalidOperationException>(() =>
            AuthenticationConfiguration.ConfigureJwtBearer(options, configuration));
    }

    [Fact]
    public void Validates_a_locally_signed_token_with_the_production_parameter_shape()
    {
        using var trustedKey = RSA.Create(2048);
        using var otherKey = RSA.Create(2048);
        var tenantId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var issuer = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Audiences:0"] = "api://leafledger",
            })
            .Build();
        var parameters = AuthenticationConfiguration.CreateTokenValidationParameters(configuration);
        parameters.IssuerSigningKey = new RsaSecurityKey(trustedKey);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim("oid", "cccccccc-cccc-cccc-cccc-cccccccccccc"),
                new Claim("tid", tenantId),
            ]),
            Issuer = issuer,
            Audience = "api://leafledger",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(trustedKey), SecurityAlgorithms.RsaSha256),
        });

        var principal = handler.ValidateToken(token, parameters, out _);
        Assert.True(principal.Identity?.IsAuthenticated);

        var wrongKeyParameters = parameters.Clone();
        wrongKeyParameters.IssuerSigningKey = new RsaSecurityKey(otherKey);
        Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(token, wrongKeyParameters, out _));
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
                        new Claim("tid", "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                        new Claim("scope", "ledger.write profile"),
                    ],
                    "test")),
            },
        };

        var user = new HttpContextCurrentUser(accessor);

        Assert.True(user.IsAuthenticated);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), user.SubjectId);
        Assert.Equal("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", user.TenantId);
        Assert.True(user.HasScope("ledger.write"));
        Assert.True(user.HasScope("profile"));
    }

    [Fact]
    public void Uses_oid_for_the_consumers_tenant_because_the_personal_account_sub_is_not_a_guid()
    {
        // Real personal Microsoft account (consumers tenant) tokens carry a non-GUID
        // pairwise `sub` and a GUID `oid`. The stable GUID identity must come from `oid`.
        var user = CreateUser(
            ("oid", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ("sub", "AAAAAAAAAAAAAAAAAAAAAINCyGVXH3sXAr9DcJThgg"),
            ("tid", "9188040d-6c67-4c5b-b112-36a304b66dad"));

        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), user.SubjectId);
    }

    [Fact]
    public void Uses_oid_for_an_organization_token_and_sub_when_oid_is_absent()
    {
        var organizationUser = CreateUser(
            ("oid", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ("sub", "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ("tid", "cccccccc-cccc-cccc-cccc-cccccccccccc"));
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), organizationUser.SubjectId);

        var fallbackUser = CreateUser(
            ("sub", "dddddddd-dddd-dddd-dddd-dddddddddddd"),
            ("tid", "cccccccc-cccc-cccc-cccc-cccccccccccc"));

        Assert.Equal(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), fallbackUser.SubjectId);
    }

    private static HttpContextCurrentUser CreateUser(params (string Type, string Value)[] claims)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    claims.Select(claim => new Claim(claim.Type, claim.Value)),
                    "test")),
            },
        };

        return new HttpContextCurrentUser(accessor);
    }
}