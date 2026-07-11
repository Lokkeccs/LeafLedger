using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace LeafLedger.Host.Authorization;

public static class AuthenticationConfiguration
{
    public const string DefaultRequiredScope = "ledger.write";

    public static TokenValidationParameters CreateTokenValidationParameters(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("Authentication");
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidateIssuer = true,
            ValidIssuers = ReadValues(section, "ValidIssuers"),
            ValidateAudience = true,
            ValidAudiences = ReadValues(section, "Audiences"),
            ValidateLifetime = true,
        };
    }

    public static void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("Authentication");
        options.Authority = section["Authority"];
        options.MapInboundClaims = false;
        options.TokenValidationParameters = CreateTokenValidationParameters(configuration);
    }

    public static string GetRequiredScope(IConfiguration configuration) =>
        configuration.GetSection("Authentication")["RequiredScope"]?.Trim() is { Length: > 0 } scope
            ? scope
            : DefaultRequiredScope;

    private static string[] ReadValues(IConfigurationSection section, string key) =>
        section.GetSection(key).GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();
}