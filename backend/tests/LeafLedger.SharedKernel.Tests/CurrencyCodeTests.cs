using LeafLedger.SharedKernel;
using Xunit;

namespace LeafLedger.SharedKernel.Tests;

public class CurrencyCodeTests
{
    [Theory]
    [InlineData("CHF", 2)]
    [InlineData("EUR", 2)]
    [InlineData("USD", 2)]
    [InlineData("JPY", 0)]
    public void Accepts_supported_codes_with_correct_exponent(string code, int expectedExponent)
    {
        var currency = CurrencyCode.Parse(code);
        Assert.Equal(code, currency.Code);
        Assert.Equal(expectedExponent, currency.MinorUnitExponent);
    }

    [Fact]
    public void Normalizes_case()
    {
        Assert.Equal(CurrencyCode.Chf, CurrencyCode.Parse("chf"));
    }

    [Theory]
    [InlineData("CH")]
    [InlineData("CHFX")]
    [InlineData("123")]
    [InlineData("XYZ")]
    [InlineData(null)]
    public void Rejects_invalid_or_unsupported_codes(string? code)
    {
        Assert.True(CurrencyCode.TryParse(code).IsFailure);
        Assert.Throws<ArgumentException>(() => CurrencyCode.Parse(code!));
    }
}
