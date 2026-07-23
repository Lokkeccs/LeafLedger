using Microsoft.AspNetCore.Http;

namespace LeafLedger.Modules.Ledger.Application.Accounts;

public sealed record CreateAccountCommand(
    Guid GroupId,
    int Code,
    string Name,
    string Currency,
    string Kind,
    bool IsActive = true,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null,
    string? FxPolicy = null);

public sealed record UpdateAccountCommand(
    Guid GroupId,
    int Code,
    string Name,
    string Currency,
    string Kind,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null,
    string? FxPolicy = null);

public sealed record SetAccountActiveCommand;

public sealed record CreateGroupCommand(
    string Name,
    int RangeStart,
    int RangeEnd,
    Guid? ParentId = null,
    string? FxPolicy = null);

public sealed record UpdateGroupCommand(
    string Name,
    int RangeStart,
    int RangeEnd,
    Guid? ParentId = null,
    string? FxPolicy = null);

public sealed record GroupView(
    Guid Id,
    string Name,
    int RangeStart,
    int RangeEnd,
    Guid? ParentId,
    string? FxPolicy);

public sealed record GroupCatalogReport(Guid SpaceId, IReadOnlyList<GroupView> Groups);

public sealed record AccountManagementIssue(string Code, string Message, string? Field = null);

public sealed record AccountManagementFailure(int Status, IReadOnlyList<AccountManagementIssue> Issues);

public sealed record AccountManagementReplay(int Status, string Body);

#pragma warning disable CA1000
public readonly record struct AccountManagementOutcome<T>(
    T? Value,
    AccountManagementFailure? Failure,
    AccountManagementReplay? Replay = null,
    int SuccessStatus = StatusCodes.Status200OK)
    where T : class
{
    public bool IsSuccess => Value is not null;

    public bool IsReplay => Replay is not null;

    public static AccountManagementOutcome<T> Success(T value, int status = StatusCodes.Status200OK) =>
        new(value, null, null, status);

    public static AccountManagementOutcome<T> Failed(int status, params AccountManagementIssue[] issues) =>
        new(null, new AccountManagementFailure(status, issues));

    public static AccountManagementOutcome<T> Replayed(AccountManagementReplay replay) =>
        new(null, null, replay);
}

public interface IAccountManagementService
{
    Task<AccountManagementOutcome<AccountView>> CreateAccountAsync(
        Guid spaceId,
        Guid actorId,
        string idempotencyKey,
        CreateAccountCommand command,
        CancellationToken cancellationToken = default);

    Task<AccountManagementOutcome<AccountView>> UpdateAccountAsync(
        Guid spaceId,
        Guid actorId,
        Guid accountId,
        string idempotencyKey,
        UpdateAccountCommand command,
        CancellationToken cancellationToken = default);

    Task<AccountManagementOutcome<AccountView>> SetAccountActiveAsync(
        Guid spaceId,
        Guid actorId,
        Guid accountId,
        bool active,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AccountManagementOutcome<GroupView>> CreateAccountGroupAsync(
        Guid spaceId,
        Guid actorId,
        string idempotencyKey,
        CreateGroupCommand command,
        CancellationToken cancellationToken = default);

    Task<AccountManagementOutcome<GroupView>> UpdateAccountGroupAsync(
        Guid spaceId,
        Guid actorId,
        Guid groupId,
        string idempotencyKey,
        UpdateGroupCommand command,
        CancellationToken cancellationToken = default);
}

public interface IGroupCatalogService
{
    Task<GroupCatalogReport> GetGroupsAsync(Guid spaceId, CancellationToken cancellationToken = default);
}
#pragma warning restore CA1000