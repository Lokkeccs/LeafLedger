using System.Collections.Frozen;

namespace LeafLedger.SharedKernel;

/// <summary>
/// An ISO-4217 alpha-3 currency code together with its minor-unit exponent
/// (the number of decimal places the currency subdivides into).
/// Only currencies in the supported set can be constructed.
/// </summary>
public readonly record struct CurrencyCode
{
    // Minor-unit exponents per ISO 4217. Extend deliberately as the product needs more.
    private static readonly FrozenDictionary<string, int> Supported =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["CHF"] = 2,
            ["EUR"] = 2,
            ["USD"] = 2,
            ["JPY"] = 0,
        }.ToFrozenDictionary();

    private CurrencyCode(string code, int minorUnitExponent)
    {
        Code = code;
        MinorUnitExponent = minorUnitExponent;
    }

    /// <summary>The three-letter uppercase ISO-4217 code, e.g. "CHF".</summary>
    public string Code { get; }

    /// <summary>Number of decimal places (2 for CHF/EUR/USD, 0 for JPY).</summary>
    public int MinorUnitExponent { get; }

    public static CurrencyCode Chf => Parse("CHF");

    public static CurrencyCode Eur => Parse("EUR");

    public static CurrencyCode Usd => Parse("USD");

    public static CurrencyCode Jpy => Parse("JPY");

    public static Result<CurrencyCode> TryParse(string? code)
    {
        if (code is null || code.Length != 3)
        {
            return Result<CurrencyCode>.Failure(
                new DomainError("currency.invalid_format", "Currency code must be three letters."));
        }

        var upper = code.ToUpperInvariant();
        return Supported.TryGetValue(upper, out var exponent)
            ? Result<CurrencyCode>.Success(new CurrencyCode(upper, exponent))
            : Result<CurrencyCode>.Failure(
                new DomainError("currency.unsupported", $"Currency '{upper}' is not supported."));
    }

    public static CurrencyCode Parse(string code)
    {
        var result = TryParse(code);
        return result.IsSuccess
            ? result.Value
            : throw new ArgumentException(result.Error!.Message, nameof(code));
    }

    public override string ToString() => Code;
}
