using System.Globalization;
using LeafLedger.Modules.ChartOfAccounts.Domain.Accounts;
using LeafLedger.Modules.ChartOfAccounts.Domain.CurrencyPolicy;
using LeafLedger.Modules.Ledger.Application.Posting;
using LeafLedger.Modules.Ledger.Domain.Journal;
using LeafLedger.Modules.Ledger.Domain.Periods;
using LeafLedger.Modules.Ledger.Domain.PostingValidity;
using LeafLedger.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeafLedger.Modules.Ledger.Infrastructure;

internal sealed class JournalPostingService : IJournalPostingService
{
    private readonly LedgerDbContext _db;
    private readonly IdempotencyMetrics _metrics;
    private readonly IReportRefreshQueue _refreshQueue;

    public JournalPostingService(LedgerDbContext db, IdempotencyMetrics metrics, IReportRefreshQueue refreshQueue)
    {
        _db = db;
        _metrics = metrics;
        _refreshQueue = refreshQueue;
    }

    public Task<PostingOutcome> PostAsync(PostJournalEntryCommand command, CancellationToken cancellationToken = default) =>
        ExecuteAsync(command.SpaceId, command.ActorId, command.IdempotencyKey, "post", IdempotencyStore.Hash(command), async (tx, ct) =>
            await PostWithinTransactionAsync(command, tx, ct).ConfigureAwait(false), cancellationToken);

    public Task<PostingOutcome> ReverseAsync(ReverseJournalEntryCommand command, CancellationToken cancellationToken = default) =>
        ExecuteAsync(command.SpaceId, command.ActorId, command.IdempotencyKey, $"reverse:{command.EntryId:D}", IdempotencyStore.Hash(command), async (tx, ct) =>
            await ReverseWithinTransactionAsync(command, tx, ct).ConfigureAwait(false), cancellationToken);

    private async Task<PostingOutcome> ExecuteAsync(
        Guid spaceId,
        Guid actorId,
        string? idempotencyKey,
        string target,
        byte[] requestHash,
        Func<NpgsqlTransaction, CancellationToken, Task<PostingOutcome>> operation,
        CancellationToken cancellationToken)
    {
        await using var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        _db.Database.UseTransaction(transaction);
        await BindTransactionAsync(connection, transaction, spaceId, actorId, cancellationToken).ConfigureAwait(false);

        Guid? key = null;
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            try
            {
                key = IdempotencyStore.ParseKey(idempotencyKey);
            }
            catch (FormatException)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return PostingOutcome.Failed(400, new PostingIssue("idempotency.key_invalid", "Idempotency-Key must be a valid ULID."));
            }

            await AcquireKeyLockAsync(spaceId, key.Value, transaction, cancellationToken).ConfigureAwait(false);
            var existing = await IdempotencyStore.FindLiveAsync(transaction, spaceId, key.Value, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                if (IdempotencyStore.IsSameRequest(existing, requestHash))
                {
                    return PostingOutcome.Replayed(new IdempotencyReplay(existing.ResponseStatus, existing.ResponseBody));
                }

                _metrics.RecordCollision(spaceId);
                return PostingOutcome.Failed(409, new PostingIssue("idempotency.key_reused", "The idempotency key was already used for a different request."));
            }

            await IdempotencyStore.DeleteExpiredAsync(transaction, spaceId, key.Value, cancellationToken).ConfigureAwait(false);
        }

        var outcome = await operation(transaction, cancellationToken).ConfigureAwait(false);
        if (!outcome.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return outcome;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (key is not null)
        {
            await IdempotencyStore.InsertAsync(
                transaction,
                spaceId,
                key.Value,
                actorId,
                target,
                requestHash,
                StatusCodes.Status201Created,
                IdempotencyStore.SerializeResponse(outcome.Value!),
                cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _refreshQueue.TryEnqueue(spaceId);

        return outcome;
    }

    private static async Task AcquireKeyLockAsync(Guid spaceId, Guid key, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));",
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("lock_key", $"{spaceId:D}:{key:D}");
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<PostingOutcome> PostWithinTransactionAsync(
        PostJournalEntryCommand command,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Description) || command.Lines is null || command.Lines.Count < 2)
        {
            return PostingOutcome.Failed(400, new PostingIssue("request.invalid", "Description and at least two lines are required."));
        }

        var accountIds = command.Lines.Select(line => line.AccountId).Distinct().ToArray();
        var accounts = await _db.Accounts.Where(account => account.SpaceId == command.SpaceId && accountIds.Contains(account.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var space = await _db.Spaces.SingleOrDefaultAsync(item => item.Id == command.SpaceId, cancellationToken).ConfigureAwait(false);
        if (space is null)
        {
            return PostingOutcome.Failed(422, new PostingIssue("space.not_found", "The space does not exist."));
        }

        var currencyIssues = ValidateCurrencies(command.Lines, space.BaseCurrency);
        if (currencyIssues.Count > 0)
        {
            return PostingOutcome.Failed(422, currencyIssues.ToArray());
        }

        var accountContracts = accounts.Select(ToCurrencyAccount).ToArray();
        var currencyPolicyIssues = CurrencyPolicyEvaluator.Evaluate(
            accountContracts,
            command.Lines.Select(line => new CurrencyPolicyReference(
                Id<AccountTag>.FromStorage(line.AccountId), line.Currency))).ToArray();
        if (currencyPolicyIssues.Length > 0)
        {
            return PostingOutcome.Failed(422, currencyPolicyIssues.Select(issue => new PostingIssue(
                "currency_policy.currency_not_allowed",
                $"Currency {issue.TransactionCurrency} is not allowed for account {issue.AccountId.ToBoundaryString()}.")).ToArray());
        }

        var references = command.Lines.Select(line => new PostingReference(line.AccountId, command.Date)).ToArray();
        var validityError = PostingValidityEvaluator.AssertPostingAccountsValid(
            PostingPurpose.Business,
            accounts.Select(account => new AccountReference(account.Id, account.IsActive, account.ValidFrom, account.ValidTo)).ToArray(),
            references);
        if (validityError is not null)
        {
            return PostingOutcome.Failed(422, validityError.Issues.Select(issue => new PostingIssue(
                $"posting_validity.{issue.Reason.ToString().ToLowerInvariant()}",
                $"Account {issue.EntityId} is not valid for posting on {command.Date:yyyy-MM-dd}.")).ToArray());
        }

        var periods = await _db.Periods.Where(period => period.SpaceId == command.SpaceId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var periodResult = PeriodStateResolver.AssertPostingPeriodOpen(command.Date, periods.Select(ToPeriodSnapshot).ToArray());
        if (!periodResult.IsOpen)
        {
            return PostingOutcome.Failed(422, new PostingIssue(periodResult.Error!.Code, periodResult.Error.Message));
        }

        var baseIssues = ValidateBaseAmounts(command.Lines, space.BaseCurrency);
        if (baseIssues.Count > 0)
        {
            return PostingOutcome.Failed(422, baseIssues.ToArray());
        }

        var entryId = Guid.NewGuid();
        var entryNo = await AllocateEntryNoAsync(command.SpaceId, transaction, cancellationToken).ConfigureAwait(false);
        var lines = new List<JournalLine>();
        foreach (var request in command.Lines)
        {
            var lineResult = JournalLine.Create(
                request.AccountId,
                request.AmountMinor,
                CurrencyCode.Parse(request.Currency!),
                request.BaseAmountMinor,
                fxRateMetadataId: null,
                request.VatCodeId,
                request.BusinessPartnerId,
                request.ProjectId,
                request.Attributions?.Select(item => new LineAttribution(item.UserId, item.SharePermille)).ToArray());
            if (lineResult.IsFailure)
            {
                return PostingOutcome.Failed(422, new PostingIssue(lineResult.Error!.Code, lineResult.Error.Message));
            }

            lines.Add(lineResult.Value);
        }

        var aggregate = JournalEntry.Create(
            Id<JournalEntryTag>.FromStorage(entryId),
            command.SpaceId,
            command.Date,
            command.Description.Trim(),
            command.Reference,
            command.ActorId,
            lines);
        if (aggregate.IsFailure)
        {
            return PostingOutcome.Failed(422, new PostingIssue(aggregate.Error!.Code, aggregate.Error.Message));
        }

        AddEntities(aggregate.Value, entryNo, command.Lines, space.BaseCurrency);
        return PostingOutcome.Success(new PostingResponse(entryId, entryNo, command.Date, null));
    }

    private async Task<PostingOutcome> ReverseWithinTransactionAsync(
        ReverseJournalEntryCommand command,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var source = await _db.JournalEntries.SingleOrDefaultAsync(entry => entry.SpaceId == command.SpaceId && entry.Id == command.EntryId, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return PostingOutcome.Failed(404, new PostingIssue("journal_entry.not_found", "The journal entry does not exist in this space."));
        }

        if (await _db.JournalEntries.AnyAsync(entry => entry.SpaceId == command.SpaceId && entry.ReversesEntryId == command.EntryId, cancellationToken).ConfigureAwait(false))
        {
            return PostingOutcome.Failed(422, new PostingIssue("journal_entry.already_reversed", "The journal entry has already been reversed."));
        }

        var originalLines = await _db.JournalLines.Where(line => line.EntryId == source.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var lineIds = originalLines.Select(line => line.Id).ToArray();
        var attributions = await _db.LineAttributions
            .Where(attribution => lineIds.Contains(attribution.LineId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var periods = await _db.Periods.Where(period => period.SpaceId == command.SpaceId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var periodResult = PeriodStateResolver.AssertPostingPeriodOpen(command.Date, periods.Select(ToPeriodSnapshot).ToArray());
        if (!periodResult.IsOpen)
        {
            return PostingOutcome.Failed(422, new PostingIssue(periodResult.Error!.Code, periodResult.Error.Message));
        }

        var sourceLines = new List<JournalLine>(originalLines.Count);
        foreach (var line in originalLines)
        {
            var lineResult = JournalLine.Create(
                line.AccountId,
                line.AmountMinor,
                CurrencyCode.Parse(line.Currency),
                line.BaseAmountMinor,
                fxRateMetadataId: null,
                line.VatCodeId,
                line.BusinessPartnerId,
                line.ProjectId,
                attributions
                    .Where(attribution => attribution.LineId == line.Id)
                    .Select(attribution => new LineAttribution(attribution.UserId, attribution.SharePermille))
                    .ToArray());
            if (lineResult.IsFailure)
            {
                return PostingOutcome.Failed(422, new PostingIssue(lineResult.Error!.Code, lineResult.Error.Message));
            }

            sourceLines.Add(lineResult.Value);
        }

        var sourceAggregate = JournalEntry.Create(
            Id<JournalEntryTag>.FromStorage(source.Id),
            source.SpaceId,
            source.Date,
            source.Description!,
            source.Reference,
            source.CreatedBy,
            sourceLines);
        if (sourceAggregate.IsFailure)
        {
            return PostingOutcome.Failed(422, new PostingIssue(sourceAggregate.Error!.Code, sourceAggregate.Error.Message));
        }

        var entryId = Guid.NewGuid();
        var reversalResult = sourceAggregate.Value.Reverse(
            command.Date,
            Id<JournalEntryTag>.FromStorage(entryId),
            command.ActorId);
        if (reversalResult.IsFailure)
        {
            return PostingOutcome.Failed(422, new PostingIssue(reversalResult.Error!.Code, reversalResult.Error.Message));
        }

        var entryNo = await AllocateEntryNoAsync(command.SpaceId, transaction, cancellationToken).ConfigureAwait(false);
        var reversal = new Entities.JournalEntry
        {
            Id = entryId, SpaceId = command.SpaceId, EntryNo = entryNo, Date = command.Date,
            Status = "posted", Description = source.Description, Reference = source.Reference,
            ReversesEntryId = source.Id, CreatedBy = command.ActorId, CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.JournalEntries.Add(reversal);
        for (var index = 0; index < originalLines.Count; index++)
        {
            var sourceLine = originalLines[index];
            var reversalLine = reversalResult.Value.Lines[index];
            var lineId = Guid.NewGuid();
            _db.JournalLines.Add(new Entities.JournalLine
            {
                Id = lineId, EntryId = entryId, SpaceId = command.SpaceId, AccountId = reversalLine.AccountId,
                AmountMinor = reversalLine.AmountMinor, Currency = reversalLine.Currency.Code, BaseAmountMinor = reversalLine.BaseAmountMinor,
                FxRate = sourceLine.FxRate, VatCodeId = reversalLine.VatCodeId, BusinessPartnerId = reversalLine.BusinessPartnerId, ProjectId = reversalLine.ProjectId,
            });
            _db.LineAttributions.AddRange(reversalLine.Attributions.Select(item => new Entities.LineAttribution
            {
                Id = Guid.NewGuid(), LineId = lineId, SpaceId = command.SpaceId, UserId = item.UserId, SharePermille = item.SharePermille,
            }));
        }

        return PostingOutcome.Success(new PostingResponse(entryId, entryNo, command.Date, source.Id));
    }

    private void AddEntities(JournalEntry aggregate, long entryNo, IReadOnlyList<PostJournalLineRequest> requests, string baseCurrency)
    {
        _db.JournalEntries.Add(new Entities.JournalEntry
        {
            Id = aggregate.Id.ToStorage(), SpaceId = aggregate.SpaceId, EntryNo = entryNo, Date = aggregate.EntryDate,
            Status = "posted", Description = aggregate.Description, Reference = aggregate.Reference,
            CreatedBy = aggregate.CreatedBy, CreatedAt = DateTimeOffset.UtcNow,
        });

        for (var index = 0; index < aggregate.Lines.Count; index++)
        {
            var domainLine = aggregate.Lines[index];
            var request = requests[index];
            var lineId = Guid.NewGuid();
            _db.JournalLines.Add(new Entities.JournalLine
            {
                Id = lineId, EntryId = aggregate.Id.ToStorage(), SpaceId = aggregate.SpaceId, AccountId = domainLine.AccountId,
                AmountMinor = domainLine.AmountMinor, Currency = domainLine.Currency.Code, BaseAmountMinor = domainLine.BaseAmountMinor,
                FxRate = ParseFxRate(request.FxRate), VatCodeId = domainLine.VatCodeId, BusinessPartnerId = domainLine.BusinessPartnerId, ProjectId = domainLine.ProjectId,
            });
            if (request.Attributions is not null)
            {
                _db.LineAttributions.AddRange(request.Attributions.Select(item => new Entities.LineAttribution
                {
                    Id = Guid.NewGuid(), LineId = lineId, SpaceId = aggregate.SpaceId, UserId = item.UserId, SharePermille = item.SharePermille,
                }));
            }
        }
    }

    private static List<PostingIssue> ValidateCurrencies(IReadOnlyList<PostJournalLineRequest> lines, string baseCurrency)
    {
        var issues = new List<PostingIssue>();
        for (var index = 0; index < lines.Count; index++)
        {
            if (CurrencyCode.TryParse(lines[index].Currency?.Trim()).IsFailure)
            {
                issues.Add(new PostingIssue("currency.invalid", "Each journal line requires a supported ISO currency.", index));
            }
        }

        return issues;
    }

    private static List<PostingIssue> ValidateBaseAmounts(IReadOnlyList<PostJournalLineRequest> lines, string baseCurrency)
    {
        var issues = new List<PostingIssue>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var result = BaseAmountValidator.Validate(line.AmountMinor, line.BaseAmountMinor, line.Currency!, baseCurrency, line.FxRate);
            if (!result.IsValid)
            {
                issues.Add(new PostingIssue(result.Code!, result.Message!, index));
            }
        }

        return issues;
    }

    private static async Task<long> AllocateEntryNoAsync(Guid spaceId, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@space::text, 0));", transaction.Connection, transaction);
        lockCommand.Parameters.AddWithValue("space", spaceId);
        await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var numberCommand = new NpgsqlCommand(
            "SELECT COALESCE((SELECT entry_no FROM journal_entries WHERE space_id = @space ORDER BY entry_no DESC LIMIT 1), 0) + 1;",
            transaction.Connection, transaction);
        numberCommand.Parameters.AddWithValue("space", spaceId);
        return (long)(await numberCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task BindTransactionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid spaceId, Guid actorId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SET LOCAL ROLE leafledger_app; SELECT set_config('app.current_space_id', @space, true); SELECT set_config('app.current_actor', @actor, true);",
            connection, transaction);
        command.Parameters.AddWithValue("space", spaceId.ToString());
        command.Parameters.AddWithValue("actor", actorId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static PeriodSnapshot ToPeriodSnapshot(Entities.Period period) =>
        new(period.Name, period.StartDate, period.EndExclusive, Enum.Parse<PeriodState>(period.State, true));

    private static CurrencyPolicyAccount ToCurrencyAccount(Entities.Account account) =>
        new(Id<AccountTag>.FromStorage(account.Id), account.Currency, Enum.Parse<AccountKind>(account.Kind, true));

    private static decimal? ParseFxRate(string? rate) =>
        string.IsNullOrWhiteSpace(rate) ? null : decimal.Parse(rate, CultureInfo.InvariantCulture);
}