namespace LeafLedger.SharedKernel;

/// <summary>Whether an accounting period still accepts postings.</summary>
public enum PeriodStatus
{
    Open,
    Closed,
}

/// <summary>
/// A half-open accounting period <c>[Start, EndExclusive)</c> over civil calendar dates,
/// plus its open/closed status. This is a pure value type: it imposes no fiscal-calendar
/// rules (fiscal-year derivation, sub-periods, close lifecycle) — those are higher-level
/// concerns that produce <see cref="Period"/> values. See WP P1-WP03 Open Question B.
/// </summary>
public readonly record struct Period : IComparable<Period>
{
    private Period(DateOnly start, DateOnly endExclusive, PeriodStatus status)
    {
        Start = start;
        EndExclusive = endExclusive;
        Status = status;
    }

    /// <summary>Inclusive start date.</summary>
    public DateOnly Start { get; }

    /// <summary>Exclusive end date; must be strictly after <see cref="Start"/>.</summary>
    public DateOnly EndExclusive { get; }

    public PeriodStatus Status { get; }

    public static Period Create(
        DateOnly start,
        DateOnly endExclusive,
        PeriodStatus status = PeriodStatus.Open)
    {
        if (start >= endExclusive)
        {
            throw new ArgumentException(
                $"Period start ({start:O}) must be strictly before its end ({endExclusive:O}).",
                nameof(start));
        }

        return new Period(start, endExclusive, status);
    }

    /// <summary>True when <paramref name="date"/> falls within <c>[Start, EndExclusive)</c>.</summary>
    public bool Contains(DateOnly date) => date >= Start && date < EndExclusive;

    public int CompareTo(Period other)
    {
        var byStart = Start.CompareTo(other.Start);
        return byStart != 0 ? byStart : EndExclusive.CompareTo(other.EndExclusive);
    }

    public static bool operator <(Period left, Period right) => left.CompareTo(right) < 0;

    public static bool operator <=(Period left, Period right) => left.CompareTo(right) <= 0;

    public static bool operator >(Period left, Period right) => left.CompareTo(right) > 0;

    public static bool operator >=(Period left, Period right) => left.CompareTo(right) >= 0;
}
