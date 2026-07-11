namespace LeafLedger.Modules.Ledger.Domain.PostingValidity;

public enum PostingPurpose
{
    Business,
    Personal,
}

public enum PostingEntityType
{
    Account,
    BusinessPartner,
    User,
    Project,
}

public sealed record AccountReference(
    Guid Id,
    bool IsActive,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null);

public sealed record TimeboundReference(
    Guid Id,
    bool IsActive,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null);

public sealed record ProjectReference(
    Guid Id,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null);

public sealed record PostingReference(Guid EntityId, DateOnly TransactionDate, string? Source = null);
