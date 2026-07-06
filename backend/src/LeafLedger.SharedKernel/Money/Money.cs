namespace LeafLedger.SharedKernel;

/// <summary>
/// An exact monetary amount: signed integer minor units plus a currency.
/// There is deliberately no floating-point anywhere in this type. Arithmetic is
/// only defined between amounts of the same currency.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    private Money(long minorUnits, CurrencyCode currency)
    {
        MinorUnits = minorUnits;
        Currency = currency;
    }

    /// <summary>Signed amount expressed in the currency's minor units (e.g. Rappen, cents).</summary>
    public long MinorUnits { get; }

    public CurrencyCode Currency { get; }

    public bool IsZero => MinorUnits == 0;

    public static Money Zero(CurrencyCode currency) => new(0, currency);

    /// <summary>Construct directly from a minor-unit count (e.g. 1050 = CHF 10.50).</summary>
    public static Money OfMinorUnits(long minorUnits, CurrencyCode currency) =>
        new(minorUnits, currency);

    /// <summary>
    /// Convert a decimal amount to minor units. Rounding is explicit and defaults to
    /// Swiss commercial rounding (round half away from zero); see WP P1-WP03 Open Question A.
    /// </summary>
    public static Money FromDecimal(
        decimal amount,
        CurrencyCode currency,
        MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
        var scaled = amount * Factor(currency.MinorUnitExponent);
        var minorUnits = (long)Math.Round(scaled, 0, rounding);
        return new Money(minorUnits, currency);
    }

    /// <summary>Render the amount as a decimal in major units. Edge/display use only.</summary>
    public decimal ToDecimal() => MinorUnits / Factor(Currency.MinorUnitExponent);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(checked(MinorUnits + other.MinorUnits), Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(checked(MinorUnits - other.MinorUnits), Currency);
    }

    public Money Negate() => new(checked(-MinorUnits), Currency);

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(other);
        return MinorUnits.CompareTo(other.MinorUnits);
    }

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator -(Money value) => value.Negate();

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        $"{ToDecimal().ToString($"F{Currency.MinorUnitExponent}", System.Globalization.CultureInfo.InvariantCulture)} {Currency.Code}";

    private void EnsureSameCurrency(Money other)
    {
        if (!Currency.Equals(other.Currency))
        {
            throw new InvalidOperationException(
                $"Cannot operate on different currencies: {Currency.Code} and {other.Currency.Code}.");
        }
    }

    private static decimal Factor(int exponent)
    {
        decimal factor = 1m;
        for (var i = 0; i < exponent; i++)
        {
            factor *= 10m;
        }

        return factor;
    }
}
