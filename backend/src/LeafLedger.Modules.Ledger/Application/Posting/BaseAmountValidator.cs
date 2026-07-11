using System.Numerics;

namespace LeafLedger.Modules.Ledger.Application.Posting;

public readonly record struct BaseAmountValidationResult(bool IsValid, string? Code, string? Message)
{
    public static BaseAmountValidationResult Valid() => new(true, null, null);

    public static BaseAmountValidationResult Invalid(string code, string message) => new(false, code, message);
}

public static class BaseAmountValidator
{
    public static BaseAmountValidationResult Validate(
        long amountMinor,
        long baseAmountMinor,
        string transactionCurrency,
        string baseCurrency,
        string? fxRate)
    {
        if (string.Equals(transactionCurrency.Trim(), baseCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            if (baseAmountMinor != amountMinor || !IsUnitRate(fxRate))
            {
                return BaseAmountValidationResult.Invalid(
                    "base_amount.same_currency_mismatch",
                    "A same-currency line must use the transaction amount as its base amount and an absent or unit FX rate.");
            }

            return BaseAmountValidationResult.Valid();
        }

        if (!TryParsePositiveRate(fxRate, out var numerator, out var denominator))
        {
            return BaseAmountValidationResult.Invalid(
                "base_amount.fx_rate_invalid",
                "A foreign-currency line requires a positive decimal FX rate.");
        }

        var product = (BigInteger)amountMinor * numerator;
        var rounded = AwayFromZero(product, denominator);
        if (amountMinor != 0 && (baseAmountMinor == 0 || Math.Sign(amountMinor) != Math.Sign(baseAmountMinor)))
        {
            return BaseAmountValidationResult.Invalid(
                "base_amount.sign_mismatch",
                "The base amount must have the same sign as the transaction amount.");
        }

        var difference = BigInteger.Abs((BigInteger)baseAmountMinor - rounded);
        return difference <= BigInteger.One
            ? BaseAmountValidationResult.Valid()
            : BaseAmountValidationResult.Invalid(
                "base_amount.inconsistent",
                "The supplied base amount is inconsistent with the supplied FX rate.");
    }

    private static bool IsUnitRate(string? fxRate) =>
        string.IsNullOrWhiteSpace(fxRate) ||
        (decimal.TryParse(fxRate, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed == 1m);

    private static bool TryParsePositiveRate(string? text, out BigInteger numerator, out BigInteger denominator)
    {
        numerator = BigInteger.Zero;
        denominator = BigInteger.One;
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var rate) || rate <= 0m)
        {
            return false;
        }

        var bits = decimal.GetBits(rate);
        numerator = ((BigInteger)(uint)bits[0]) |
                    ((BigInteger)(uint)bits[1] << 32) |
                    ((BigInteger)(uint)bits[2] << 64);
        var scale = (bits[3] >> 16) & 0x7F;
        denominator = BigInteger.Pow(10, scale);
        return true;
    }

    private static BigInteger AwayFromZero(BigInteger numerator, BigInteger denominator)
    {
        var quotient = BigInteger.Divide(numerator, denominator);
        var remainder = BigInteger.Abs(BigInteger.Remainder(numerator, denominator));
        return remainder * 2 >= denominator
            ? quotient + (numerator.Sign < 0 ? -BigInteger.One : BigInteger.One)
            : quotient;
    }
}