using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LeafLedger.Host.Authorization;

public static class EntraIssuerValidator
{
    public static string ValidateIssuer(
        string issuer,
        SecurityToken securityToken,
        TokenValidationParameters validationParameters) =>
        ValidateIssuer(issuer, securityToken, validationParameters, null);

    public static string ValidateIssuer(
        string issuer,
        SecurityToken securityToken,
        TokenValidationParameters validationParameters,
        IEnumerable<string>? tenantAllowlist) =>
        Validate(issuer, ReadTenantId(securityToken), tenantAllowlist);

    public static string Validate(
        string? issuer,
        string? tenantId,
        IEnumerable<string>? tenantAllowlist = null)
    {
        if (!Guid.TryParse(tenantId, out var tokenTenantId) || !TryReadTenant(issuer, out var issuerTenantId))
        {
            throw InvalidIssuer(issuer);
        }

        var allowedTenants = tenantAllowlist?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (issuerTenantId != tokenTenantId ||
            (allowedTenants is { Count: > 0 } && !allowedTenants.Contains(issuerTenantId.ToString())))
        {
            throw InvalidIssuer(issuer);
        }

        return issuer!;
    }

    private static bool TryReadTenant(string? issuer, out Guid tenantId)
    {
        tenantId = default;
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (!uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return false;
        }

        var isV2 = uri.Host.Equals("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase) &&
                   uri.AbsolutePath.EndsWith("/v2.0", StringComparison.Ordinal);
        var isV1 = uri.Host.Equals("sts.windows.net", StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.EndsWith('/');
        if (!isV1 && !isV2)
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != (isV2 ? 2 : 1) || !Guid.TryParse(segments[0], out tenantId))
        {
            return false;
        }

        var expectedPath = isV2
            ? $"/{segments[0]}/v2.0"
            : $"/{segments[0]}/";
        return uri.AbsolutePath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadTenantId(SecurityToken securityToken) => securityToken switch
    {
        JwtSecurityToken jwt => jwt.Claims.FirstOrDefault(claim => claim.Type == "tid")?.Value,
        JsonWebToken json => json.Claims.FirstOrDefault(claim => claim.Type == "tid")?.Value,
        _ => null,
    };

    private static SecurityTokenInvalidIssuerException InvalidIssuer(string? issuer) =>
        new($"Issuer '{issuer ?? "<missing>"}' is not a valid Entra issuer for the token tenant.");
}