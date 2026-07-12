using LeafLedger.SharedKernel;

namespace LeafLedger.Modules.Ledger.Domain.Periods;

public enum PeriodState
{
    Open,
    Closed,
    Locked,
    NoPeriodDefined,
}

public sealed record PeriodSnapshot(
    string Name,
    DateOnly StartDate,
    DateOnly EndExclusive,
    PeriodState State);

public sealed record PeriodClosedError(string PeriodName, PeriodState State, DateOnly TxDate);

public readonly record struct PeriodOpenResult
{
    private PeriodOpenResult(bool isOpen, DomainError? error, PeriodClosedError? closedError)
    {
        IsOpen = isOpen;
        Error = error;
        ClosedError = closedError;
    }

    public bool IsOpen { get; }

    public DomainError? Error { get; }

    public PeriodClosedError? ClosedError { get; }

    internal static PeriodOpenResult Open() => new(true, null, null);

    internal static PeriodOpenResult NoPeriod(DateOnly transactionDate) => new(
        false,
        new DomainError(
            "posting_period.not_defined",
            $"No accounting period is defined for {transactionDate:yyyy-MM-dd}."),
        null);

    internal static PeriodOpenResult NotOpen(PeriodSnapshot period, DateOnly transactionDate) => new(
        false,
        new DomainError("posting_period.not_open", $"Accounting period '{period.Name}' is {period.State}."),
        new PeriodClosedError(period.Name, period.State, transactionDate));
}
