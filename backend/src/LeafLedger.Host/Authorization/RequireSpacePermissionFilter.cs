using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LeafLedger.Host.Authorization;

public sealed class RequireSpacePermissionFilter : IEndpointFilter
{
    private readonly string _permission;
    private readonly string _requiredScope;

    public RequireSpacePermissionFilter(string permission, string requiredScope)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            throw new ArgumentException("A permission is required.", nameof(permission));
        }

        if (string.IsNullOrWhiteSpace(requiredScope))
        {
            throw new ArgumentException("A required scope is required.", nameof(requiredScope));
        }

        _permission = permission;
        _requiredScope = requiredScope;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var currentUser = httpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!currentUser.IsAuthenticated || currentUser.SubjectId is not Guid subjectId)
        {
            return AuthorizationProblem(StatusCodes.Status401Unauthorized, "auth.unauthenticated", "Authentication is required.");
        }

        if (!Guid.TryParse(currentUser.TenantId, out var tenantId))
        {
            return AuthorizationProblem(StatusCodes.Status403Forbidden, "auth.identity_unresolved", "The authenticated identity has no valid tenant.");
        }

        if (!currentUser.HasScope(_requiredScope))
        {
            return AuthorizationProblem(StatusCodes.Status403Forbidden, "auth.forbidden", "The access scope is not valid for this operation.");
        }

        if (!TryGetSpaceId(httpContext, out var spaceId))
        {
            return AuthorizationProblem(StatusCodes.Status403Forbidden, "auth.forbidden", "The requested space is not available.");
        }

        var identityResolver = httpContext.RequestServices.GetRequiredService<IIdentityResolver>();
        var userId = await identityResolver.ResolveUserIdAsync(subjectId, tenantId, httpContext.RequestAborted).ConfigureAwait(false);

        var entitlement = httpContext.RequestServices.GetRequiredService<ILicenseEntitlement>();
        if (!await entitlement.IsEntitledAsync(userId, spaceId, _permission, httpContext.RequestAborted).ConfigureAwait(false))
        {
            return AuthorizationProblem(StatusCodes.Status403Forbidden, "auth.license_inactive", "The current license does not permit this operation.");
        }

        var membershipQuery = httpContext.RequestServices.GetRequiredService<ISpaceMembershipQuery>();
        var roleValue = await membershipQuery.GetRoleAsync(spaceId, userId, httpContext.RequestAborted).ConfigureAwait(false);
        if (roleValue is null)
        {
            return AuthorizationProblem(StatusCodes.Status403Forbidden, "auth.not_a_member", "The current user is not a member of this space.");
        }

        if (!SpaceRoleParser.TryParse(roleValue, out var role) || !ModulePermissions.Allows(role, _permission))
        {
            return AuthorizationProblem(StatusCodes.Status403Forbidden, "auth.permission_denied", "The current role does not grant this permission.");
        }

        httpContext.Items[AuthorizationContext.ItemKey] = new AuthorizationContext(spaceId, userId, role);
        httpContext.Items[LedgerRequestContext.ActorIdItemKey] = userId;
        return await next(context).ConfigureAwait(false);
    }

    private static bool TryGetSpaceId(HttpContext context, out Guid spaceId)
    {
        spaceId = Guid.Empty;
        return context.Request.RouteValues.TryGetValue("spaceId", out var value) &&
            Guid.TryParse(value?.ToString(), out spaceId);
    }

    private static IResult AuthorizationProblem(int status, string code, string title)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = "https://leafledger.dev/problems/authorization",
        };
        problem.Extensions["code"] = code;
        return Results.Problem(problem);
    }
}

public sealed record AuthorizationContext(Guid SpaceId, Guid SubjectId, SpaceRole Role)
{
    public const string ItemKey = "LeafLedger.AuthorizationContext";
}

public static class AuthorizationEndpointExtensions
{
    public static RouteHandlerBuilder RequireSpacePermission(
        this RouteHandlerBuilder builder,
        string permission,
        string requiredScope) =>
        builder.AddEndpointFilter(new RequireSpacePermissionFilter(permission, requiredScope));
}