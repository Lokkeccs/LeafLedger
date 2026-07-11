using System.Security.Claims;

namespace LeafLedger.Host.Authorization;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? SubjectId { get; }

    IReadOnlySet<string> Scopes { get; }

    bool HasScope(string scope);
}

public sealed class HttpContextCurrentUser : ICurrentUser
{
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
            var subject = principal?.FindFirstValue("oid") ?? principal?.FindFirstValue("sub");
            return Guid.TryParse(subject, out var id) ? id : null;
        }
    }

    public IReadOnlySet<string> Scopes =>
        (_httpContextAccessor.HttpContext?.User.Claims ?? Enumerable.Empty<Claim>())
            .Where(claim => claim.Type is "scope" or "scp")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.Ordinal);

    public bool HasScope(string scope) => Scopes.Contains(scope);
}