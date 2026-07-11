namespace LeafLedger.Host.Authorization;

public sealed class AllowAllLicenseEntitlement : ILicenseEntitlement
{
    public Task<bool> IsEntitledAsync(
        Guid subjectId,
        Guid spaceId,
        string permission,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}