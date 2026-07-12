namespace LeafLedger.Modules.Ledger.Domain.Periods;

public readonly record struct PeriodTransitionResult(bool IsAllowed, string? Reason)
{
    public static PeriodTransitionResult Allowed() => new(true, null);

    public static PeriodTransitionResult Rejected(string reason) => new(false, reason);
}

public readonly record struct PeriodRangeResult(bool IsValid, string? Reason)
{
    public static PeriodRangeResult Valid() => new(true, null);

    public static PeriodRangeResult Invalid(string reason) => new(false, reason);
}

public static class PeriodLifecycle
{
    public static PeriodTransitionResult CanTransition(PeriodState from, PeriodState to) =>
        from switch
        {
            PeriodState.Open when to is PeriodState.Closed or PeriodState.Locked => PeriodTransitionResult.Allowed(),
            PeriodState.Closed when to is PeriodState.Open or PeriodState.Locked => PeriodTransitionResult.Allowed(),
            PeriodState.Locked => PeriodTransitionResult.Rejected("period.locked"),
            _ => PeriodTransitionResult.Rejected("period.invalid_transition"),
        };

    public static PeriodRangeResult ValidateRange(DateOnly startDate, DateOnly endExclusive) =>
        startDate < endExclusive
            ? PeriodRangeResult.Valid()
            : PeriodRangeResult.Invalid("period.invalid_range");
}