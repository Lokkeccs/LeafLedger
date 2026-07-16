using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LeafLedger.Modules.Ledger.Infrastructure;

[Authorize]
public sealed class SpaceInvalidationHub : Hub
{
    private readonly IIdentityResolver _identityResolver;
    private readonly ISpaceMembershipQuery _membershipQuery;

    public SpaceInvalidationHub(
        IIdentityResolver identityResolver,
        ISpaceMembershipQuery membershipQuery)
    {
        _identityResolver = identityResolver;
        _membershipQuery = membershipQuery;
    }

    public override async Task OnConnectedAsync()
    {
        if (!Guid.TryParse(Context.GetHttpContext()?.Request.Query["spaceId"], out var spaceId) ||
            !TryGetTenantId(out var tenantId) ||
            !TryGetSubjectId(out var subjectId))
        {
            Context.Abort();
            return;
        }

        Guid userId;
        try
        {
            userId = await _identityResolver
                .ResolveUserIdAsync(subjectId, tenantId, Context.ConnectionAborted)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            Context.Abort();
            return;
        }

        var role = await _membershipQuery
            .GetRoleAsync(spaceId, userId, Context.ConnectionAborted)
            .ConfigureAwait(false);
        if (role is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(spaceId)).ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public static string GroupName(Guid spaceId) => $"space:{spaceId:D}";

    private bool TryGetTenantId(out Guid tenantId) =>
        Guid.TryParse(Context.User?.FindFirstValue("tid"), out tenantId);

    private bool TryGetSubjectId(out Guid subjectId)
    {
        var subject = Context.User?.FindFirstValue("oid") ?? Context.User?.FindFirstValue("sub");
        return Guid.TryParse(subject, out subjectId);
    }
}
