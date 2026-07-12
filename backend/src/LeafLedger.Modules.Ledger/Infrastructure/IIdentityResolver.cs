namespace LeafLedger.Modules.Ledger.Infrastructure;

public interface IIdentityResolver
{
    Task<Guid> ResolveUserIdAsync(Guid subject, Guid tenantId, CancellationToken cancellationToken = default);
}