using System.Data;
using LeafLedger.Modules.Ledger.Application.Periods;
using LeafLedger.Modules.Ledger.Domain.Periods;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeafLedger.Modules.Ledger.Infrastructure;

internal sealed class PeriodLifecycleService : IPeriodLifecycleService
{
    private readonly LedgerDbContext _db;
    private readonly IdempotencyMetrics _metrics;

    public PeriodLifecycleService(LedgerDbContext db, IdempotencyMetrics metrics)
    {
        _db = db;
        _metrics = metrics;
    }

    public Task<PeriodOutcome> CreateAsync(Guid spaceId, Guid actorId, CreatePeriodRequest request, string idempotencyKey, CancellationToken cancellationToken = default) =>
        ExecuteAsync(spaceId, actorId, idempotencyKey, "period.create", request, async (transaction, ct) =>
        {
            var range = PeriodLifecycle.ValidateRange(request.StartDate, request.EndExclusive);
            if (!range.IsValid)
            {
                return PeriodOutcome.Failed(422, new PeriodIssue(range.Reason!, "The period range must be non-empty."));
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return PeriodOutcome.Failed(422, new PeriodIssue("period.invalid_name", "A period name is required."));
            }

            if (await _db.Periods.AnyAsync(period => period.SpaceId == spaceId && period.StartDate < request.EndExclusive && request.StartDate < period.EndExclusive, ct).ConfigureAwait(false))
            {
                return PeriodOutcome.Failed(409, new PeriodIssue("period.overlap", "The period overlaps an existing period."));
            }

            var entity = new Entities.Period
            {
                Id = Guid.NewGuid(), SpaceId = spaceId, Name = request.Name.Trim(),
                StartDate = request.StartDate, EndExclusive = request.EndExclusive,
                State = "open", CreatedAt = DateTimeOffset.UtcNow,
            };
            _db.Periods.Add(entity);
            return PeriodOutcome.Success(ToResponse(entity));
        }, cancellationToken);

    public Task<PeriodOutcome> TransitionAsync(Guid spaceId, Guid actorId, Guid periodId, PeriodState targetState, string idempotencyKey, CancellationToken cancellationToken = default) =>
        ExecuteAsync(spaceId, actorId, idempotencyKey, $"period.{targetState.ToString().ToLowerInvariant()}:{periodId:D}", new { periodId, targetState }, async (transaction, ct) =>
        {
            var period = await _db.Periods.SingleOrDefaultAsync(item => item.SpaceId == spaceId && item.Id == periodId, ct).ConfigureAwait(false);
            if (period is null)
            {
                return PeriodOutcome.Failed(404, new PeriodIssue("period.not_found", "The accounting period does not exist in this space."));
            }

            var currentState = ParseState(period.State);
            if (currentState == PeriodState.Locked)
            {
                return PeriodOutcome.Failed(422, new PeriodIssue("period.locked", "A locked accounting period is terminal."));
            }

            var transition = PeriodLifecycle.CanTransition(currentState, targetState);
            if (!transition.IsAllowed)
            {
                return PeriodOutcome.Failed(422, new PeriodIssue(transition.Reason!, "The accounting period state transition is not allowed."));
            }

            period.State = ToWireValue(targetState);
            return PeriodOutcome.Success(ToResponse(period), StatusCodes.Status200OK);
        }, cancellationToken);

    public async Task<IReadOnlyList<PeriodResponse>> ListAsync(Guid spaceId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        _db.Database.UseTransaction(transaction);
        await BindTransactionAsync(connection, transaction, spaceId, actorId, cancellationToken).ConfigureAwait(false);
        var periods = await _db.Periods.Where(period => period.SpaceId == spaceId).OrderBy(period => period.StartDate).ToListAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return periods.Select(ToResponse).ToArray();
    }

    public Task<PeriodOutcome> BootstrapOpenPeriodAsync(Guid spaceId, Guid actorId, DateOnly fiscalYearStart, string idempotencyKey, CancellationToken cancellationToken = default) =>
        ExecuteAsync(spaceId, actorId, idempotencyKey, "period.bootstrap", fiscalYearStart, async (transaction, ct) =>
        {
            var endExclusive = fiscalYearStart.AddYears(1);
            var existing = await _db.Periods.SingleOrDefaultAsync(period =>
                period.SpaceId == spaceId &&
                period.StartDate == fiscalYearStart &&
                period.EndExclusive == endExclusive, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return PeriodOutcome.Success(ToResponse(existing));
            }

            if (await _db.Periods.AnyAsync(period => period.SpaceId == spaceId && period.StartDate < endExclusive && fiscalYearStart < period.EndExclusive, ct).ConfigureAwait(false))
            {
                return PeriodOutcome.Failed(409, new PeriodIssue("period.overlap", "The bootstrap period overlaps an existing period."));
            }

            var entity = new Entities.Period
            {
                Id = Guid.NewGuid(), SpaceId = spaceId, Name = $"Fiscal year {fiscalYearStart.Year}",
                StartDate = fiscalYearStart, EndExclusive = endExclusive,
                State = "open", CreatedAt = DateTimeOffset.UtcNow,
            };
            _db.Periods.Add(entity);
            return PeriodOutcome.Success(ToResponse(entity));
        }, cancellationToken);

    private async Task<PeriodOutcome> ExecuteAsync<TRequest>(Guid spaceId, Guid actorId, string idempotencyKey, string target, TRequest request, Func<NpgsqlTransaction, CancellationToken, Task<PeriodOutcome>> operation, CancellationToken cancellationToken)
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
            return PeriodOutcome.Failed(400, new PeriodIssue("idempotency.key_invalid", "Idempotency-Key must be a valid ULID."));
        }

        var requestHash = IdempotencyStore.HashPeriod(target, request!);
        await AcquireKeyLockAsync(spaceId, key, transaction, cancellationToken).ConfigureAwait(false);
        var existing = await IdempotencyStore.FindLiveAsync(transaction, spaceId, key, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            if (IdempotencyStore.IsSameRequest(existing, requestHash))
            {
                return PeriodOutcome.Replayed(new PeriodIdempotencyReplay(existing.ResponseStatus, existing.ResponseBody));
            }

            _metrics.RecordCollision(spaceId);
            return PeriodOutcome.Failed(409, new PeriodIssue("idempotency.key_reused", "The idempotency key was already used for a different request."));
        }

        await IdempotencyStore.DeleteExpiredAsync(transaction, spaceId, key, cancellationToken).ConfigureAwait(false);
        try
        {
            var outcome = await operation(transaction, cancellationToken).ConfigureAwait(false);
            if (!outcome.IsSuccess)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return outcome;
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await IdempotencyStore.InsertAsync(transaction, spaceId, key, actorId, target, requestHash, outcome.SuccessStatus, IdempotencyStore.SerializeResponse(outcome.Value!), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return outcome;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgresException && postgresException.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return PeriodOutcome.Failed(409, new PeriodIssue("period.overlap", "The period overlaps an existing period."));
        }
    }

    private static async Task AcquireKeyLockAsync(Guid spaceId, Guid key, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", transaction.Connection, transaction);
        command.Parameters.AddWithValue("lock_key", $"{spaceId:D}:{key:D}");
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task BindTransactionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid spaceId, Guid actorId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SET LOCAL ROLE leafledger_app; SELECT set_config('app.current_space_id', @space, true); SELECT set_config('app.current_actor', @actor, true);", connection, transaction);
        command.Parameters.AddWithValue("space", spaceId.ToString());
        command.Parameters.AddWithValue("actor", actorId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static PeriodResponse ToResponse(Entities.Period period) => new(period.Id, period.Name, period.StartDate, period.EndExclusive, period.State);

    private static PeriodState ParseState(string value) => Enum.Parse<PeriodState>(value, true);

    private static string ToWireValue(PeriodState value) => value.ToString().ToLowerInvariant();
}