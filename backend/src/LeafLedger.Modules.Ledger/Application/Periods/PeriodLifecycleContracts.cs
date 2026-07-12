using LeafLedger.Modules.Ledger.Domain.Periods;
using Microsoft.AspNetCore.Http;

namespace LeafLedger.Modules.Ledger.Application.Periods;

public sealed record CreatePeriodRequest(string Name, DateOnly StartDate, DateOnly EndExclusive);

public sealed record PeriodResponse(Guid Id, string Name, DateOnly StartDate, DateOnly EndExclusive, string State);

public sealed record PeriodIssue(string Code, string Message);

public sealed record PeriodFailure(int Status, IReadOnlyList<PeriodIssue> Issues);

public sealed record PeriodIdempotencyReplay(int Status, string Body);

public readonly record struct PeriodOutcome(PeriodResponse? Value, PeriodFailure? Failure, PeriodIdempotencyReplay? Replay = null, int SuccessStatus = StatusCodes.Status201Created)
{
    public bool IsSuccess => Value is not null;

    public bool IsReplay => Replay is not null;

    public static PeriodOutcome Success(PeriodResponse value, int status = StatusCodes.Status201Created) => new(value, null, null, status);

    public static PeriodOutcome Failed(int status, params PeriodIssue[] issues) => new(null, new PeriodFailure(status, issues));

    public static PeriodOutcome Replayed(PeriodIdempotencyReplay replay) => new(null, null, replay);
}

public interface IPeriodLifecycleService
{
    Task<PeriodOutcome> CreateAsync(Guid spaceId, Guid actorId, CreatePeriodRequest request, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<PeriodOutcome> TransitionAsync(Guid spaceId, Guid actorId, Guid periodId, PeriodState targetState, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PeriodResponse>> ListAsync(Guid spaceId, Guid actorId, CancellationToken cancellationToken = default);

    Task<PeriodOutcome> BootstrapOpenPeriodAsync(Guid spaceId, Guid actorId, DateOnly fiscalYearStart, string idempotencyKey, CancellationToken cancellationToken = default);
}