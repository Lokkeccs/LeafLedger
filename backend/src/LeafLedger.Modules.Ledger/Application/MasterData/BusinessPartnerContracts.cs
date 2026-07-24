using Microsoft.AspNetCore.Http;

namespace LeafLedger.Modules.Ledger.Application.MasterData;

public sealed record CreateBusinessPartnerCommand(
    string Name,
    string Type,
    bool IsActive = true,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null,
    string? PartnerNumber = null,
    string? CountryCode = null,
    string? Notes = null);

public sealed record UpdateBusinessPartnerCommand(
    string Name,
    string Type,
    bool IsActive,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null,
    string? PartnerNumber = null,
    string? CountryCode = null,
    string? Notes = null,
    string Version = "");

public sealed record BusinessPartnerView(
    Guid Id,
    string Name,
    string? PartnerNumber,
    string Type,
    string? CountryCode,
    bool IsActive,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    string? Notes,
    string Version);

public sealed record BusinessPartnerCatalogReport(Guid SpaceId, IReadOnlyList<BusinessPartnerView> Partners);

public sealed record BusinessPartnerIssue(string Code, string Message, string? Field = null, BusinessPartnerView? Current = null);

public sealed record BusinessPartnerFailure(int Status, IReadOnlyList<BusinessPartnerIssue> Issues);

public sealed record BusinessPartnerReplay(int Status, string Body);

#pragma warning disable CA1000
public readonly record struct BusinessPartnerOutcome<T>(
    T? Value,
    BusinessPartnerFailure? Failure,
    BusinessPartnerReplay? Replay = null,
    int SuccessStatus = StatusCodes.Status200OK)
    where T : class
{
    public bool IsSuccess => Value is not null;
    public bool IsReplay => Replay is not null;

    public static BusinessPartnerOutcome<T> Success(T value, int status = StatusCodes.Status200OK) => new(value, null, null, status);
    public static BusinessPartnerOutcome<T> Failed(int status, params BusinessPartnerIssue[] issues) => new(null, new BusinessPartnerFailure(status, issues));
    public static BusinessPartnerOutcome<T> Replayed(BusinessPartnerReplay replay) => new(null, null, replay);
}
#pragma warning restore CA1000

public interface IBusinessPartnerService
{
    Task<BusinessPartnerCatalogReport> GetBusinessPartnersAsync(Guid spaceId, CancellationToken cancellationToken = default);
    Task<BusinessPartnerView?> GetBusinessPartnerAsync(Guid spaceId, Guid partnerId, CancellationToken cancellationToken = default);
    Task<BusinessPartnerOutcome<BusinessPartnerView>> CreateAsync(Guid spaceId, Guid actorId, string idempotencyKey, CreateBusinessPartnerCommand command, CancellationToken cancellationToken = default);
    Task<BusinessPartnerOutcome<BusinessPartnerView>> UpdateAsync(Guid spaceId, Guid actorId, Guid partnerId, string idempotencyKey, UpdateBusinessPartnerCommand command, CancellationToken cancellationToken = default);
    Task<BusinessPartnerOutcome<BusinessPartnerView>> DeleteAsync(Guid spaceId, Guid actorId, Guid partnerId, string idempotencyKey, CancellationToken cancellationToken = default);
}