namespace LeafLedger.Modules.Ledger.Application.Accounts;

public sealed record AccountImportRow(
    string Kind,
    int Code,
    string Name,
    string Currency,
    string? Group,
    bool IsActive,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    string? FxPolicy);

public sealed record GroupImportRow(
    string Name,
    int RangeStart,
    int RangeEnd,
    string? Parent,
    string? FxPolicy);

public sealed record AccountImportRequest(AccountImportRow[] Rows);

public sealed record GroupImportRequest(GroupImportRow[] Rows);

public sealed record CsvImportRow<T>(int RowNumber, T Value, IReadOnlyList<string> Warnings);

public sealed record CsvImportDocument<T>(IReadOnlyList<CsvImportRow<T>> Rows, IReadOnlyList<string> Warnings);

public sealed record ImportRowResult(
    int RowNumber,
    string Outcome,
    IReadOnlyList<AccountManagementIssue> Errors,
    IReadOnlyList<string> Warnings);

public sealed record ImportReport(
    int Total,
    int Created,
    int Updated,
    int Failed,
    IReadOnlyList<ImportRowResult> Rows);

public readonly record struct AccountImportOutcome(
    ImportReport? Report,
    AccountManagementFailure? Failure,
    AccountManagementReplay? Replay = null,
    int SuccessStatus = Microsoft.AspNetCore.Http.StatusCodes.Status200OK)
{
    public bool IsSuccess => Report is not null && Failure is null && SuccessStatus < 300;

    public bool IsReplay => Replay is not null;

    public static AccountImportOutcome Success(ImportReport report) => new(report, null);

    public static AccountImportOutcome Failed(int status, params AccountManagementIssue[] issues) =>
        new(null, new AccountManagementFailure(status, issues));

    public static AccountImportOutcome Replayed(AccountManagementReplay replay) => new(null, null, replay);
}

public interface IAccountImportService
{
    Task<AccountImportOutcome> ImportAccountsAsync(
        Guid spaceId,
        Guid actorId,
        string idempotencyKey,
        IReadOnlyList<CsvImportRow<AccountImportRow>> rows,
        CancellationToken cancellationToken = default);

    Task<AccountImportOutcome> ImportGroupsAsync(
        Guid spaceId,
        Guid actorId,
        string idempotencyKey,
        IReadOnlyList<CsvImportRow<GroupImportRow>> rows,
        CancellationToken cancellationToken = default);
}