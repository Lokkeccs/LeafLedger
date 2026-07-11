namespace LeafLedger.Modules.Ledger.Infrastructure;

public interface ISpaceMembershipQuery
{
    Task<string?> GetRoleAsync(Guid spaceId, Guid userId, CancellationToken cancellationToken = default);
}