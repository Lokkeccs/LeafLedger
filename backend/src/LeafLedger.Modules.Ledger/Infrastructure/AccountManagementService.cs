using System.Data;
using LeafLedger.Modules.ChartOfAccounts.Domain.Accounts;
using LeafLedger.Modules.ChartOfAccounts.Domain.Groups;
using LeafLedger.Modules.Ledger.Application.Accounts;
using LeafLedger.Modules.Ledger.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using CoaAccount = LeafLedger.Modules.ChartOfAccounts.Domain.Accounts.Account;
using CoaAccountGroup = LeafLedger.Modules.ChartOfAccounts.Domain.Groups.AccountGroup;
using LedgerAccount = LeafLedger.Modules.Ledger.Infrastructure.Entities.Account;
using LedgerAccountGroup = LeafLedger.Modules.Ledger.Infrastructure.Entities.AccountGroup;

namespace LeafLedger.Modules.Ledger.Infrastructure;

internal sealed class AccountManagementService : IAccountManagementService, IGroupCatalogService, IAccountImportService
{
    private readonly LedgerDbContext _db;
    private readonly IdempotencyMetrics _metrics;
    private readonly ISpaceInvalidationQueue _invalidationQueue;

    public AccountManagementService(
        LedgerDbContext db,
        IdempotencyMetrics metrics,
        ISpaceInvalidationQueue invalidationQueue)
    {
        _db = db;
        _metrics = metrics;
        _invalidationQueue = invalidationQueue;
    }

    public Task<AccountManagementOutcome<AccountView>> CreateAccountAsync(
        Guid spaceId,
        Guid actorId,
        string idempotencyKey,
        CreateAccountCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(spaceId, actorId, idempotencyKey, "account.create", command, StatusCodes.Status201Created,
            (transaction, ct) => CreateAccountWithinTransactionAsync(spaceId, command, ct), cancellationToken);

    public Task<AccountManagementOutcome<AccountView>> UpdateAccountAsync(
        Guid spaceId,
        Guid actorId,
        Guid accountId,
        string idempotencyKey,
        UpdateAccountCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(spaceId, actorId, idempotencyKey, $"account.update:{accountId:D}", command, StatusCodes.Status200OK,
            (transaction, ct) => UpdateAccountWithinTransactionAsync(spaceId, accountId, command, ct), cancellationToken);

    public Task<AccountManagementOutcome<AccountView>> SetAccountActiveAsync(
        Guid spaceId,
        Guid actorId,
        Guid accountId,
        bool active,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(spaceId, actorId, idempotencyKey, $"account.{(active ? "activate" : "deactivate")}:{accountId:D}",
            new { accountId, active }, StatusCodes.Status200OK,
            (transaction, ct) => SetAccountActiveWithinTransactionAsync(spaceId, accountId, active, ct), cancellationToken);

    public Task<AccountManagementOutcome<GroupView>> CreateAccountGroupAsync(
        Guid spaceId,
        Guid actorId,
        string idempotencyKey,
        CreateGroupCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(spaceId, actorId, idempotencyKey, "group.create", command, StatusCodes.Status201Created,
            (transaction, ct) => CreateGroupWithinTransactionAsync(spaceId, command, ct), cancellationToken);

    public Task<AccountManagementOutcome<GroupView>> UpdateAccountGroupAsync(
        Guid spaceId,
        Guid actorId,
        Guid groupId,
        string idempotencyKey,
        UpdateGroupCommand command,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(spaceId, actorId, idempotencyKey, $"group.update:{groupId:D}", command, StatusCodes.Status200OK,
            (transaction, ct) => UpdateGroupWithinTransactionAsync(spaceId, groupId, command, ct), cancellationToken);

    public Task<AccountImportOutcome> ImportAccountsAsync(
        Guid spaceId,
        Guid actorId,
        string idempotencyKey,
        IReadOnlyList<CsvImportRow<AccountImportRow>> rows,
        CancellationToken cancellationToken = default) =>
        ImportAsync(spaceId, actorId, idempotencyKey, "account.import", rows,
            (row, ct) => ApplyAccountImportRowAsync(spaceId, row, ct), cancellationToken);

    public Task<AccountImportOutcome> ImportGroupsAsync(
        Guid spaceId,
        Guid actorId,
        string idempotencyKey,
        IReadOnlyList<CsvImportRow<GroupImportRow>> rows,
        CancellationToken cancellationToken = default) =>
        ImportAsync(spaceId, actorId, idempotencyKey, "group.import", rows,
            (row, ct) => ApplyGroupImportRowAsync(spaceId, row, ct), cancellationToken);

    public async Task<GroupCatalogReport> GetGroupsAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await BindTransactionAsync(connection, transaction, spaceId, Guid.Empty, cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT id, name, code_range, parent_id, fx_policy FROM account_groups ORDER BY lower(code_range), id;",
            connection,
            transaction);
        var groups = new List<GroupView>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                groups.Add(ToGroupView(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetFieldValue<NpgsqlRange<int>>(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new GroupCatalogReport(spaceId, groups);
    }

    private async Task<AccountImportOutcome> ImportAsync<T>(
        Guid spaceId,
        Guid actorId,
        string idempotencyKey,
        string target,
        IReadOnlyList<CsvImportRow<T>> rows,
        Func<CsvImportRow<T>, CancellationToken, Task<ImportRowResult>> apply,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        _db.Database.UseTransaction(transaction);
        await BindTransactionAsync(connection, transaction, spaceId, actorId, cancellationToken).ConfigureAwait(false);

        Guid key;
        try
        {
            key = IdempotencyStore.ParseKey(idempotencyKey);
        }
        catch (FormatException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return AccountImportOutcome.Failed(400,
                new AccountManagementIssue("idempotency.key_invalid", "Idempotency-Key must be a valid ULID."));
        }

        var requestHash = IdempotencyStore.HashPeriod(target, rows.Select(row => row.Value).ToArray());
        await AcquireKeyLockAsync(spaceId, key, transaction, cancellationToken).ConfigureAwait(false);
        var existing = await IdempotencyStore.FindLiveAsync(transaction, spaceId, key, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            if (IdempotencyStore.IsSameRequest(existing, requestHash))
            {
                return AccountImportOutcome.Replayed(new AccountManagementReplay(existing.ResponseStatus, existing.ResponseBody));
            }

            _metrics.RecordCollision(spaceId);
            return AccountImportOutcome.Failed(409,
                new AccountManagementIssue("idempotency.key_reused", "The idempotency key was already used for a different request."));
        }

        await IdempotencyStore.DeleteExpiredAsync(transaction, spaceId, key, cancellationToken).ConfigureAwait(false);
        var results = new List<ImportRowResult>(rows.Count);
        foreach (var row in rows)
        {
            results.Add(await apply(row, cancellationToken).ConfigureAwait(false));
        }

        var report = CreateImportReport(results);
        if (report.Failed > 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new AccountImportOutcome(report, new AccountManagementFailure(422, []), null, 422);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await IdempotencyStore.InsertAsync(
                transaction,
                spaceId,
                key,
                actorId,
                target,
                requestHash,
                StatusCodes.Status200OK,
                IdempotencyStore.SerializeResponse(report),
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _invalidationQueue.TryEnqueue(spaceId, InvalidationTopics.AccountCatalogTopics);
            return AccountImportOutcome.Success(report);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres &&
            (postgres.SqlState == PostgresErrorCodes.UniqueViolation || postgres.SqlState == PostgresErrorCodes.ExclusionViolation))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return AccountImportOutcome.Failed(422, postgres.SqlState == PostgresErrorCodes.ExclusionViolation
                ? new AccountManagementIssue("group.code_range_overlap", "The group code range overlaps a sibling group.", "rangeStart")
                : new AccountManagementIssue("account.code_taken", "The account code is already used in this space.", "code"));
        }
    }

    private async Task<ImportRowResult> ApplyAccountImportRowAsync(
        Guid spaceId,
        CsvImportRow<AccountImportRow> row,
        CancellationToken cancellationToken)
    {
        var group = row.Value.Group is null
            ? null
            : await _db.AccountGroups.SingleOrDefaultAsync(item => item.SpaceId == spaceId && item.Name == row.Value.Group, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return FailedImportRow(row.RowNumber, row.Warnings,
                new AccountManagementIssue("account.group_unknown", "The account group does not exist in this space.", "group"));
        }

        var existing = await _db.Accounts.SingleOrDefaultAsync(
            item => item.SpaceId == spaceId && item.Code == row.Value.Code,
            cancellationToken).ConfigureAwait(false);
        AccountManagementOutcome<AccountView> outcome;
        if (existing is null)
        {
            outcome = await CreateAccountWithinTransactionAsync(spaceId, new CreateAccountCommand(
                group.Id, row.Value.Code, row.Value.Name, row.Value.Currency, row.Value.Kind,
                row.Value.IsActive, row.Value.ValidFrom, row.Value.ValidTo, row.Value.FxPolicy), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            outcome = await UpdateAccountWithinTransactionAsync(spaceId, existing.Id, new UpdateAccountCommand(
                group.Id, row.Value.Code, row.Value.Name, row.Value.Currency, row.Value.Kind,
                row.Value.ValidFrom, row.Value.ValidTo, row.Value.FxPolicy), cancellationToken).ConfigureAwait(false);
            if (outcome.IsSuccess && existing.IsActive != row.Value.IsActive)
            {
                outcome = await SetAccountActiveWithinTransactionAsync(spaceId, existing.Id, row.Value.IsActive, cancellationToken).ConfigureAwait(false);
            }
        }

        return outcome.IsSuccess
            ? new ImportRowResult(row.RowNumber, existing is null ? "created" : "updated", [], row.Warnings)
            : FailedImportRow(row.RowNumber, row.Warnings, outcome.Failure!.Issues.ToArray());
    }

    private async Task<ImportRowResult> ApplyGroupImportRowAsync(
        Guid spaceId,
        CsvImportRow<GroupImportRow> row,
        CancellationToken cancellationToken)
    {
        Guid? parentId = null;
        if (!string.IsNullOrWhiteSpace(row.Value.Parent))
        {
            parentId = await _db.AccountGroups
                .Where(item => item.SpaceId == spaceId && item.Name == row.Value.Parent)
                .Select(item => (Guid?)item.Id)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (parentId is null)
            {
                return FailedImportRow(row.RowNumber, row.Warnings,
                    new AccountManagementIssue("group.parent_not_found", "The parent group does not exist in this space.", "parent"));
            }
        }

        var existing = await _db.AccountGroups.SingleOrDefaultAsync(
            item => item.SpaceId == spaceId && item.Name == row.Value.Name,
            cancellationToken).ConfigureAwait(false);
        AccountManagementOutcome<GroupView> outcome = existing is null
            ? await CreateGroupWithinTransactionAsync(spaceId, new CreateGroupCommand(
                row.Value.Name, row.Value.RangeStart, row.Value.RangeEnd, parentId, row.Value.FxPolicy), cancellationToken).ConfigureAwait(false)
            : await UpdateGroupWithinTransactionAsync(spaceId, existing.Id, new UpdateGroupCommand(
                row.Value.Name, row.Value.RangeStart, row.Value.RangeEnd, parentId, row.Value.FxPolicy), cancellationToken).ConfigureAwait(false);

        return outcome.IsSuccess
            ? new ImportRowResult(row.RowNumber, existing is null ? "created" : "updated", [], row.Warnings)
            : FailedImportRow(row.RowNumber, row.Warnings, outcome.Failure!.Issues.ToArray());
    }

    private static ImportRowResult FailedImportRow(int rowNumber, IReadOnlyList<string> warnings, params AccountManagementIssue[] issues) =>
        new(rowNumber, "failed", issues, warnings);

    private static ImportReport CreateImportReport(List<ImportRowResult> rows) =>
        new(rows.Count,
            rows.Count(row => row.Outcome == "created"),
            rows.Count(row => row.Outcome == "updated"),
            rows.Count(row => row.Outcome == "failed"),
            rows);

    private async Task<AccountManagementOutcome<T>> ExecuteAsync<T>(
        Guid spaceId,
        Guid actorId,
        string idempotencyKey,
        string target,
        object request,
        int successStatus,
        Func<NpgsqlTransaction, CancellationToken, Task<AccountManagementOutcome<T>>> operation,
        CancellationToken cancellationToken)
        where T : class
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        _db.Database.UseTransaction(transaction);
        await BindTransactionAsync(connection, transaction, spaceId, actorId, cancellationToken).ConfigureAwait(false);

        Guid key;
        try
        {
            key = IdempotencyStore.ParseKey(idempotencyKey);
        }
        catch (FormatException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return AccountManagementOutcome<T>.Failed(400,
                new AccountManagementIssue("idempotency.key_invalid", "Idempotency-Key must be a valid ULID."));
        }

        var requestHash = IdempotencyStore.HashPeriod(target, request);
        await AcquireKeyLockAsync(spaceId, key, transaction, cancellationToken).ConfigureAwait(false);
        var existing = await IdempotencyStore.FindLiveAsync(transaction, spaceId, key, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            if (IdempotencyStore.IsSameRequest(existing, requestHash))
            {
                return AccountManagementOutcome<T>.Replayed(new AccountManagementReplay(existing.ResponseStatus, existing.ResponseBody));
            }

            _metrics.RecordCollision(spaceId);
            return AccountManagementOutcome<T>.Failed(409,
                new AccountManagementIssue("idempotency.key_reused", "The idempotency key was already used for a different request."));
        }

        await IdempotencyStore.DeleteExpiredAsync(transaction, spaceId, key, cancellationToken).ConfigureAwait(false);
        var outcome = await operation(transaction, cancellationToken).ConfigureAwait(false);
        if (!outcome.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return outcome;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await IdempotencyStore.InsertAsync(
                transaction,
                spaceId,
                key,
                actorId,
                target,
                requestHash,
                successStatus,
                IdempotencyStore.SerializeResponse(outcome.Value!),
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return AccountManagementOutcome<T>.Success(outcome.Value!, successStatus);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres &&
            (postgres.SqlState == PostgresErrorCodes.UniqueViolation || postgres.SqlState == PostgresErrorCodes.ExclusionViolation))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return postgres.SqlState switch
            {
                PostgresErrorCodes.UniqueViolation => AccountManagementOutcome<T>.Failed(422,
                    new AccountManagementIssue("account.code_taken", "The account code is already used in this space.", "code")),
                PostgresErrorCodes.ExclusionViolation => AccountManagementOutcome<T>.Failed(422,
                    new AccountManagementIssue("group.code_range_overlap", "The group code range overlaps a sibling group.", "rangeStart")),
                    _ => throw new InvalidOperationException("Unexpected account-management constraint violation.", exception),
            };
        }
    }

    private async Task<AccountManagementOutcome<AccountView>> CreateAccountWithinTransactionAsync(
        Guid spaceId,
        CreateAccountCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryParseKind(command.Kind, out var kind, out var kindIssue))
        {
            return AccountManagementOutcome<AccountView>.Failed(422, kindIssue!);
        }

        var group = await _db.AccountGroups.SingleOrDefaultAsync(
            item => item.SpaceId == spaceId && item.Id == command.GroupId,
            cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return AccountManagementOutcome<AccountView>.Failed(422,
                new AccountManagementIssue("account.group_not_found", "The account group does not exist in this space.", "groupId"));
        }

        var range = ToDomainRange(group.CodeRange);
        if (!range.Contains(command.Code))
        {
            return AccountManagementOutcome<AccountView>.Failed(422,
                new AccountManagementIssue("account.code_out_of_group_range", "The account code must be inside the group range.", "code"));
        }

        var id = Guid.NewGuid();
        var domain = CoaAccount.Create(
            LeafLedger.SharedKernel.Id<AccountTag>.FromStorage(id),
            LeafLedger.SharedKernel.Id<AccountGroupTag>.FromStorage(command.GroupId),
            command.Code,
            command.Name,
            command.Currency,
            kind,
            command.IsActive,
            command.ValidFrom,
            command.ValidTo);
        if (domain.IsFailure)
        {
            return DomainFailure<AccountView>(domain.Error!);
        }

        var entity = new LedgerAccount
        {
            Id = id,
            SpaceId = spaceId,
            GroupId = command.GroupId,
            Code = command.Code,
            Name = domain.Value.Name,
            Currency = domain.Value.Currency.ToString(),
            Kind = kind.ToString().ToLowerInvariant(),
            IsActive = command.IsActive,
            ValidFrom = command.ValidFrom,
            ValidTo = command.ValidTo,
            FxPolicy = command.FxPolicy,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Accounts.Add(entity);
        return AccountManagementOutcome<AccountView>.Success(ToAccountView(entity));
    }

    private async Task<AccountManagementOutcome<AccountView>> UpdateAccountWithinTransactionAsync(
        Guid spaceId,
        Guid accountId,
        UpdateAccountCommand command,
        CancellationToken cancellationToken)
    {
        var entity = await _db.Accounts.SingleOrDefaultAsync(
            item => item.SpaceId == spaceId && item.Id == accountId,
            cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return AccountManagementOutcome<AccountView>.Failed(404,
                new AccountManagementIssue("account.not_found", "The account does not exist in this space."));
        }

        var posted = await _db.JournalLines.AnyAsync(line =>
            line.SpaceId == spaceId && line.AccountId == accountId &&
            _db.JournalEntries.Any(entry => entry.Id == line.EntryId && entry.Status == "posted"), cancellationToken).ConfigureAwait(false);
        if (posted && (entity.Code != command.Code || !string.Equals(entity.Currency, command.Currency, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(entity.Kind, command.Kind, StringComparison.OrdinalIgnoreCase) || entity.ValidFrom != command.ValidFrom ||
            entity.ValidTo != command.ValidTo || entity.FxPolicy != command.FxPolicy || entity.GroupId != command.GroupId))
        {
            return AccountManagementOutcome<AccountView>.Failed(422,
                new AccountManagementIssue("account.field_immutable_after_posting", "This account field cannot change after its first posted journal line."));
        }

        if (!TryParseKind(command.Kind, out var kind, out var kindIssue))
        {
            return AccountManagementOutcome<AccountView>.Failed(422, kindIssue!);
        }

        var group = await _db.AccountGroups.SingleOrDefaultAsync(
            item => item.SpaceId == spaceId && item.Id == command.GroupId,
            cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return AccountManagementOutcome<AccountView>.Failed(422,
                new AccountManagementIssue("account.group_not_found", "The account group does not exist in this space.", "groupId"));
        }

        if (!ToDomainRange(group.CodeRange).Contains(command.Code))
        {
            return AccountManagementOutcome<AccountView>.Failed(422,
                new AccountManagementIssue("account.code_out_of_group_range", "The account code must be inside the group range.", "code"));
        }

        var domain = CoaAccount.Create(
            LeafLedger.SharedKernel.Id<AccountTag>.FromStorage(accountId),
            LeafLedger.SharedKernel.Id<AccountGroupTag>.FromStorage(command.GroupId),
            command.Code,
            command.Name,
            command.Currency,
            kind,
            entity.IsActive,
            command.ValidFrom,
            command.ValidTo);
        if (domain.IsFailure)
        {
            return DomainFailure<AccountView>(domain.Error!);
        }

        entity.GroupId = command.GroupId;
        entity.Code = command.Code;
        entity.Name = domain.Value.Name;
        entity.Currency = domain.Value.Currency.ToString();
        entity.Kind = kind.ToString().ToLowerInvariant();
        entity.ValidFrom = command.ValidFrom;
        entity.ValidTo = command.ValidTo;
        entity.FxPolicy = command.FxPolicy;
        return AccountManagementOutcome<AccountView>.Success(ToAccountView(entity));
    }

    private async Task<AccountManagementOutcome<AccountView>> SetAccountActiveWithinTransactionAsync(
        Guid spaceId,
        Guid accountId,
        bool active,
        CancellationToken cancellationToken)
    {
        var entity = await _db.Accounts.SingleOrDefaultAsync(
            item => item.SpaceId == spaceId && item.Id == accountId,
            cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return AccountManagementOutcome<AccountView>.Failed(404,
                new AccountManagementIssue("account.not_found", "The account does not exist in this space."));
        }

        entity.IsActive = active;
        return AccountManagementOutcome<AccountView>.Success(ToAccountView(entity));
    }

    private async Task<AccountManagementOutcome<GroupView>> CreateGroupWithinTransactionAsync(
        Guid spaceId,
        CreateGroupCommand command,
        CancellationToken cancellationToken)
    {
        var rangeResult = AccountCodeRange.Create(command.RangeStart, command.RangeEnd);
        if (rangeResult.IsFailure)
        {
            return DomainFailure<GroupView>(rangeResult.Error!);
        }

        var domain = CoaAccountGroup.Create(
            LeafLedger.SharedKernel.Id<AccountGroupTag>.New(),
            command.Name,
            rangeResult.Value,
            command.ParentId is Guid parentId
                ? LeafLedger.SharedKernel.Id<AccountGroupTag>.FromStorage(parentId)
                : null);
        if (domain.IsFailure)
        {
            return DomainFailure<GroupView>(domain.Error!);
        }

        var entity = new LedgerAccountGroup
        {
            Id = domain.Value.Id.ToStorage(),
            SpaceId = spaceId,
            CodeRange = ToPersistenceRange(rangeResult.Value),
            Name = domain.Value.Name,
            ParentId = command.ParentId,
            FxPolicy = command.FxPolicy,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.AccountGroups.Add(entity);
        return AccountManagementOutcome<GroupView>.Success(ToGroupView(entity));
    }

    private async Task<AccountManagementOutcome<GroupView>> UpdateGroupWithinTransactionAsync(
        Guid spaceId,
        Guid groupId,
        UpdateGroupCommand command,
        CancellationToken cancellationToken)
    {
        var entity = await _db.AccountGroups.SingleOrDefaultAsync(
            item => item.SpaceId == spaceId && item.Id == groupId,
            cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return AccountManagementOutcome<GroupView>.Failed(404,
                new AccountManagementIssue("group.not_found", "The group does not exist in this space."));
        }

        var rangeResult = AccountCodeRange.Create(command.RangeStart, command.RangeEnd);
        if (rangeResult.IsFailure)
        {
            return DomainFailure<GroupView>(rangeResult.Error!);
        }

        var members = await _db.Accounts.Where(account => account.SpaceId == spaceId && account.GroupId == groupId)
            .Select(account => account.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (members.Any(code => !rangeResult.Value.Contains(code)))
        {
            return AccountManagementOutcome<GroupView>.Failed(422,
                new AccountManagementIssue("group.range_excludes_member", "The group range must retain every member account code.", "rangeStart"));
        }

        var domain = CoaAccountGroup.Create(
            LeafLedger.SharedKernel.Id<AccountGroupTag>.FromStorage(groupId),
            command.Name,
            rangeResult.Value,
            command.ParentId is Guid parentId
                ? LeafLedger.SharedKernel.Id<AccountGroupTag>.FromStorage(parentId)
                : null);
        if (domain.IsFailure)
        {
            return DomainFailure<GroupView>(domain.Error!);
        }

        entity.CodeRange = ToPersistenceRange(rangeResult.Value);
        entity.Name = domain.Value.Name;
        entity.ParentId = command.ParentId;
        entity.FxPolicy = command.FxPolicy;
        return AccountManagementOutcome<GroupView>.Success(ToGroupView(entity));
    }

    private static AccountManagementOutcome<T> DomainFailure<T>(LeafLedger.SharedKernel.DomainError error)
        where T : class =>
        AccountManagementOutcome<T>.Failed(422, new AccountManagementIssue(error.Code, error.Message));

    private static bool TryParseKind(string value, out AccountKind kind, out AccountManagementIssue? issue)
    {
        if (Enum.TryParse(value, true, out kind) && Enum.IsDefined(kind))
        {
            issue = null;
            return true;
        }

        issue = new AccountManagementIssue("account.kind_invalid", "Account kind is invalid.", "kind");
        return false;
    }

    private static AccountCodeRange ToDomainRange(NpgsqlRange<int> range) =>
        AccountCodeRange.Create(range.LowerBound, range.UpperBound).Value;

    private static NpgsqlRange<int> ToPersistenceRange(AccountCodeRange range) =>
        new(range.Start, true, range.End, true);

    private static AccountView ToAccountView(LedgerAccount account) => new(
        account.Id,
        account.Code,
        account.Name,
        account.Currency.Trim(),
        account.Kind,
        account.IsActive,
        account.GroupId,
        account.ValidFrom,
        account.ValidTo,
        account.FxPolicy);

    private static GroupView ToGroupView(LedgerAccountGroup group) =>
        ToGroupView(group.Id, group.Name, group.CodeRange, group.ParentId, group.FxPolicy);

    private static GroupView ToGroupView(Guid id, string name, NpgsqlRange<int> range, Guid? parentId, string? fxPolicy) =>
        new(id, name, range.LowerBound, range.UpperBound, parentId, fxPolicy);

    private static async Task AcquireKeyLockAsync(Guid spaceId, Guid key, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));",
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("lock_key", $"{spaceId:D}:{key:D}");
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task BindTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid spaceId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SET LOCAL ROLE leafledger_app; SELECT set_config('app.current_space_id', @space, true); SELECT set_config('app.current_actor', @actor, true);",
            connection,
            transaction);
        command.Parameters.AddWithValue("space", spaceId.ToString());
        command.Parameters.AddWithValue("actor", actorId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}