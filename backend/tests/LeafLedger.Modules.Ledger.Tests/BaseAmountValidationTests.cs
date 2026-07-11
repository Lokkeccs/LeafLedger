using LeafLedger.Modules.Ledger.Application.Posting;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public sealed class BaseAmountValidationTests
{
    [Fact]
    public void Foreign_currency_uses_away_from_zero_at_half_minor_boundary()
    {
        var result = BaseAmountValidator.Validate(
            amountMinor: -1,
            baseAmountMinor: -2,
            transactionCurrency: "USD",
            baseCurrency: "CHF",
            fxRate: "1.5");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Foreign_currency_rejects_conversion_beyond_one_minor_unit()
    {
        var result = BaseAmountValidator.Validate(
            amountMinor: 100,
            baseAmountMinor: 130,
            transactionCurrency: "USD",
            baseCurrency: "CHF",
            fxRate: "1.25");

        Assert.False(result.IsValid);
        Assert.Equal("base_amount.inconsistent", result.Code);
    }

    [Fact]
    public void Same_currency_requires_exact_base_amount_and_no_non_unit_rate()
    {
        var result = BaseAmountValidator.Validate(
            amountMinor: 100,
            baseAmountMinor: 101,
            transactionCurrency: "CHF",
            baseCurrency: "CHF",
            fxRate: "1.01");

        Assert.False(result.IsValid);
        Assert.Equal("base_amount.same_currency_mismatch", result.Code);
    }

    [Fact]
    public void Foreign_currency_rejects_zero_base_amount_for_nonzero_transaction()
    {
        var result = BaseAmountValidator.Validate(1, 0, "USD", "CHF", "0.5");

        Assert.False(result.IsValid);
        Assert.Equal("base_amount.sign_mismatch", result.Code);
    }
}