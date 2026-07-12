namespace LeafLedger.Modules.Ledger.Application.Posting;

public sealed record PostJournalEntryCommand(
    Guid SpaceId,
    Guid ActorId,
    DateOnly Date,
    string Description,
    string? Reference,
    IReadOnlyList<PostJournalLineRequest> Lines,
    string? IdempotencyKey = null);

public sealed record ReverseJournalEntryCommand(
    Guid SpaceId,
    Guid ActorId,
    Guid EntryId,
    DateOnly Date,
    string? IdempotencyKey = null);

public sealed record PostJournalEntryRequest(
    DateOnly Date,
    string Description,
    IReadOnlyList<PostJournalLineRequest> Lines,
    string? Reference = null);

public sealed record ReverseJournalEntryRequest(DateOnly Date);

public sealed record PostJournalLineRequest(
    Guid AccountId,
    long AmountMinor,
    string? Currency,
    long BaseAmountMinor,
    string? FxRate = null,
    Guid? VatCodeId = null,
    Guid? BusinessPartnerId = null,
    Guid? ProjectId = null,
    IReadOnlyList<LineAttributionRequest>? Attributions = null);

public sealed record LineAttributionRequest(Guid UserId, int SharePermille);

public sealed record PostingResponse(Guid Id, long EntryNo, DateOnly Date, Guid? ReversesEntryId);

public sealed record PostingIssue(string Code, string Message, int? Line = null);

public sealed record PostingFailure(int Status, IReadOnlyList<PostingIssue> Issues);

public readonly record struct PostingOutcome(PostingResponse? Value, PostingFailure? Failure, IdempotencyReplay? Replay = null)
{
    public bool IsSuccess => Value is not null;

    public bool IsReplay => Replay is not null;

    public static PostingOutcome Success(PostingResponse value) => new(value, null);

    public static PostingOutcome Failed(int status, params PostingIssue[] issues) =>
        new(null, new PostingFailure(status, issues));

    public static PostingOutcome Replayed(IdempotencyReplay replay) =>
        new(null, null, replay);
}

public sealed record IdempotencyReplay(int Status, string Body);

public interface IJournalPostingService
{
    Task<PostingOutcome> PostAsync(PostJournalEntryCommand command, CancellationToken cancellationToken = default);

    Task<PostingOutcome> ReverseAsync(ReverseJournalEntryCommand command, CancellationToken cancellationToken = default);
}