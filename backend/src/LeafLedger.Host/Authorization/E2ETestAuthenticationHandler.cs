using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeafLedger.Host.Authorization;

public sealed class E2ETestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string AuthenticationScheme = "E2E";
    private const string TokenPrefix = "e2e:";

    private readonly IConfiguration _configuration;

    public E2ETestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder) =>
        _configuration = configuration;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadBearerToken();
        if (token is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal) ||
            token.Length != TokenPrefix.Length + 1)
        {
            return Task.FromResult(AuthenticateResult.Fail("The E2E bearer token is invalid."));
        }

        var member = token[TokenPrefix.Length..];
        var memberSection = _configuration.GetSection($"Authentication:E2E:Members:{member}");
        if (!Guid.TryParse(memberSection["Subject"], out var subject) ||
            !Guid.TryParse(memberSection["Tenant"], out var tenant))
        {
            return Task.FromResult(AuthenticateResult.Fail("The E2E member is not configured."));
        }

        var scope = AuthenticationConfiguration.GetRequiredScope(_configuration);
        var claims = new List<Claim>
        {
            new("oid", subject.ToString()),
            new("sub", subject.ToString()),
            new("tid", tenant.ToString()),
            new("scope", scope),
        };
        var identity = new ClaimsIdentity(claims, AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthenticationScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string? ReadBearerToken()
    {
        if (Request.Headers.TryGetValue("Authorization", out var authorization) &&
            authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization.ToString()["Bearer ".Length..];
        }

        return Request.Query.TryGetValue("access_token", out var accessToken)
            ? accessToken.ToString()
            : null;
    }
}