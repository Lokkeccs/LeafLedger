namespace LeafLedger.Host.Authorization;

public interface ILicenseEntitlement
{
    Task<bool> IsEntitledAsync(
        Guid subjectId,
        Guid spaceId,
        string permission,
        CancellationToken cancellationToken = default);
}