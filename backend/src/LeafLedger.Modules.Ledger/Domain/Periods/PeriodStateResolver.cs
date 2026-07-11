namespace LeafLedger.Modules.Ledger.Domain.Periods;

public static class PeriodStateResolver
{
    public static PeriodState GetEffectivePeriodState(
        DateOnly transactionDate,
        IReadOnlyCollection<PeriodSnapshot> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);
        var period = FindPeriod(transactionDate, periods);
        return period?.State ?? PeriodState.NoPeriodDefined;
    }

    public static PeriodOpenResult AssertPostingPeriodOpen(
        DateOnly transactionDate,
        IReadOnlyCollection<PeriodSnapshot> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);
        var period = FindPeriod(transactionDate, periods);
        if (period is null)
        {
            return PeriodOpenResult.NoPeriod(transactionDate);
        }

        return period.State == PeriodState.Open
            ? PeriodOpenResult.Open()
            : PeriodOpenResult.NotOpen(period, transactionDate);
    }

    private static PeriodSnapshot? FindPeriod(
        DateOnly transactionDate,
        IEnumerable<PeriodSnapshot> periods) =>
        periods.FirstOrDefault(period =>
            transactionDate >= period.StartDate && transactionDate <= period.EndDate);
}
