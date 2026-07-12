using System.Security.Claims;

namespace LeafLedger.Host.Authorization;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? SubjectId { get; }

    string? TenantId { get; }

    IReadOnlySet<string> Scopes { get; }

    bool HasScope(string scope);
}

public sealed class HttpContextCurrentUser : ICurrentUser
{
    private static readonly Guid ConsumersTenantId = Guid.Parse("9188040d-6c67-4c5b-b112-36a304b66dad");
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid? SubjectId
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            var oid = principal?.FindFirstValue("oid");
            var sub = principal?.FindFirstValue("sub");
            var subject = TenantId is { } tenant && Guid.TryParse(tenant, out var tenantId) && tenantId == ConsumersTenantId
                ? sub
                : oid ?? sub;
            return Guid.TryParse(subject, out var id) ? id : null;
        }
    }

    public string? TenantId =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue("tid");

    public IReadOnlySet<string> Scopes =>
        (_httpContextAccessor.HttpContext?.User.Claims ?? Enumerable.Empty<Claim>())
            .Where(claim => claim.Type is "scope" or "scp")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.Ordinal);

    public bool HasScope(string scope) => Scopes.Contains(scope);
}