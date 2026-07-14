namespace LeafLedger.Modules.Ledger.Application.Accounts;

public interface IAccountCatalogService
{
    Task<AccountCatalogReport> GetAccountsAsync(Guid spaceId, CancellationToken cancellationToken = default);
}

public sealed record AccountView(
    Guid Id,
    int Code,
    string Name,
    string Currency,
    string Kind,
    bool IsActive,
    Guid GroupId,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    string? FxPolicy);

public sealed record AccountCatalogReport(Guid SpaceId, IReadOnlyList<AccountView> Accounts);