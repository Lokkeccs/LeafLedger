namespace LeafLedger.Modules.Ledger.Domain.PostingValidity;

public enum PostingValidityReason
{
    Missing,
    Inactive,
    Future,
    Expired,
}

public sealed record PostingValidityIssue(
    PostingEntityType EntityType,
    Guid EntityId,
    PostingValidityReason Reason,
    DateOnly TxDate,
    string? Source);

public sealed record PostingValidityError(IReadOnlyList<PostingValidityIssue> Issues);
