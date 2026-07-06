using LeafLedger.SharedKernel;
using Xunit;

namespace LeafLedger.SharedKernel.Tests;

public class MoneyTests
{
    private static readonly CurrencyCode Chf = CurrencyCode.Chf;
    private static readonly CurrencyCode Eur = CurrencyCode.Eur;
    private static readonly CurrencyCode Jpy = CurrencyCode.Jpy;

    [Fact]
    public void Equality_is_by_minor_units_and_currency()
    {
        Assert.Equal(Money.OfMinorUnits(1050, Chf), Money.OfMinorUnits(1050, Chf));
        Assert.NotEqual(Money.OfMinorUnits(1050, Chf), Money.OfMinorUnits(1050, Eur));
        Assert.NotEqual(Money.OfMinorUnits(1050, Chf), Money.OfMinorUnits(1051, Chf));
    }

    [Fact]
    public void Add_subtract_negate_operate_on_minor_units()
    {
        var a = Money.OfMinorUnits(1050, Chf);
        var b = Money.OfMinorUnits(200, Chf);

        Assert.Equal(Money.OfMinorUnits(1250, Chf), a.Add(b));
        Assert.Equal(Money.OfMinorUnits(850, Chf), a.Subtract(b));
        Assert.Equal(Money.OfMinorUnits(-1050, Chf), a.Negate());
        Assert.Equal(Money.OfMinorUnits(1250, Chf), a + b);
        Assert.Equal(Money.OfMinorUnits(850, Chf), a - b);
        Assert.Equal(Money.OfMinorUnits(-1050, Chf), -a);
    }

    [Fact]
    public void Adding_different_currencies_throws()
    {
        var chf = Money.OfMinorUnits(100, Chf);
        var eur = Money.OfMinorUnits(100, Eur);

        Assert.Throws<InvalidOperationException>(() => chf.Add(eur));
        Assert.Throws<InvalidOperationException>(() => chf.Subtract(eur));
        Assert.Throws<InvalidOperationException>(() => chf.CompareTo(eur));
    }

    [Theory]
    [InlineData("1.005", 101)]   // half rounds away from zero
    [InlineData("-1.005", -101)]
    [InlineData("10.50", 1050)]
    [InlineData("0.004", 0)]
    [InlineData("0.005", 1)]
    public void FromDecimal_uses_away_from_zero_rounding_for_two_decimals(string amount, long expectedMinor)
    {
        var money = Money.FromDecimal(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Chf);
        Assert.Equal(expectedMinor, money.MinorUnits);
    }

    [Theory]
    [InlineData("1234", 1234)]   // JPY has zero minor-unit exponent
    [InlineData("1234.4", 1234)]
    [InlineData("1234.5", 1235)]
    public void FromDecimal_respects_zero_exponent_currency(string amount, long expectedMinor)
    {
        var money = Money.FromDecimal(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Jpy);
        Assert.Equal(expectedMinor, money.MinorUnits);
    }

    [Fact]
    public void FromDecimal_ToDecimal_round_trips_for_all_minor_values()
    {
        foreach (var currency in new[] { Chf, Jpy })
        {
            for (long minor = -100_000; minor <= 100_000; minor += 137)
            {
                var money = Money.OfMinorUnits(minor, currency);
                var roundTripped = Money.FromDecimal(money.ToDecimal(), currency);
                Assert.Equal(money, roundTripped);
            }
        }
    }

    [Theory]
    [InlineData(typeof(Money))]
    [InlineData(typeof(CurrencyCode))]
    public void No_public_member_exposes_float_or_double(Type type)
    {
        var floatTypes = new[] { typeof(float), typeof(double) };

        foreach (var property in type.GetProperties())
        {
            Assert.DoesNotContain(property.PropertyType, floatTypes);
        }

        foreach (var field in type.GetFields())
        {
            Assert.DoesNotContain(field.FieldType, floatTypes);
        }

        foreach (var method in type.GetMethods())
        {
            Assert.DoesNotContain(method.ReturnType, floatTypes);
            foreach (var parameter in method.GetParameters())
            {
                Assert.DoesNotContain(parameter.ParameterType, floatTypes);
            }
        }
    }
}
