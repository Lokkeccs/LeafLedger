using System.Text.Json;
using System.Globalization;
using LeafLedger.Modules.ChartOfAccounts.Domain.Accounts;
using LeafLedger.Modules.ChartOfAccounts.Domain.Fx;
using LeafLedger.Modules.ChartOfAccounts.Tests.Fixtures;
using Xunit;

namespace LeafLedger.Modules.ChartOfAccounts.Tests;

public class FxPolicyGoldenFixtureTests
{
    public static TheoryData<LedgerCoreFixture> Cases
    {
        get
        {
            var data = new TheoryData<LedgerCoreFixture>();
            foreach (var fixture in LedgerCoreFixtureLoader.LoadSelected()
                         .Where(item => item.File.StartsWith("fx-metadata/", StringComparison.Ordinal)))
            {
                data.Add(fixture);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_old_fx_policy_fixture(LedgerCoreFixture fixture)
    {
        using var document = JsonDocument.Parse(fixture.Json);
        var input = document.RootElement.GetProperty("input");
        var expected = document.RootElement.GetProperty("expected").GetProperty("value");
        var purpose = ParsePurpose(input.GetProperty("purpose").GetString()!);

        switch (fixture.Unit)
        {
            case "resolveGroupFxPolicy":
                var groupPolicy = FxPolicyResolver.ResolveGroup(
                    purpose,
                    ParseKind(input.GetProperty("type").GetString()!),
                    input.GetProperty("group").GetString()!,
                    input.TryGetProperty("groupPolicy", out var groupOverride)
                        ? ParseOverride(groupOverride)
                        : null);
                AssertPolicy(expected, groupPolicy);
                break;
            case "resolveAccountFxPolicy":
                var accountElement = input.GetProperty("account");
                var accountPolicy = FxPolicyResolver.ResolveAccount(
                    purpose,
                    ParseAccount(accountElement),
                    input.TryGetProperty("groupPolicy", out var accountGroupOverride)
                        ? ParseOverride(accountGroupOverride)
                        : null);
                AssertPolicy(expected, accountPolicy);
                break;
            case "buildTransactionLineFxMetadata":
                var metadataAccount = input.GetProperty("account");
                var metadata = FxPolicyResolver.BuildTransactionLineMetadata(
                    purpose,
                    ParseAccount(metadataAccount),
                    DateOnly.ParseExact(input.GetProperty("txDate").GetString()!, "yyyy-MM-dd"));
                Assert.Equal(
                    expected.GetProperty("fxRateDate").GetString(),
                    metadata.RateDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                Assert.Equal(expected.GetProperty("fxRateTiming").GetString(), ToFixture(metadata.RateTiming));
                Assert.Equal(expected.GetProperty("fxTreatmentApplied").GetString(), ToFixture(metadata.Treatment));
                Assert.Equal(expected.GetProperty("fxClosingRevalueApplied").GetBoolean(), metadata.ClosingRevalue);
                Assert.Equal(expected.GetProperty("fxVatMethodApplied").GetString(), ToFixture(metadata.VatMethod));
                Assert.Equal(expected.GetProperty("fxCurrency").GetString(), metadata.Currency);
                break;
            default:
                throw new InvalidDataException($"Unsupported fixture {fixture}.");
        }
    }

    private static FxAccount ParseAccount(JsonElement element) => new(
        ParseKind(element.GetProperty("type").GetString()!),
        element.GetProperty("group").GetString()!,
        element.TryGetProperty("currency", out var currency) ? currency.GetString() : null,
        ParseOverride(element));

    private static FxPolicyOverride ParseOverride(JsonElement element) => new(
        element.TryGetProperty("fxTreatment", out var treatment) ? ParseTreatment(treatment.GetString()!) : null,
        element.TryGetProperty("fxRateTimingDefault", out var timing) ? ParseTiming(timing.GetString()!) : null,
        element.TryGetProperty("closingRevalue", out var closing) ? closing.GetBoolean() : null,
        element.TryGetProperty("vatFxMethodOverride", out var vat) ? ParseVat(vat.GetString()!) : null);

    private static void AssertPolicy(JsonElement expected, FxPolicy actual)
    {
        Assert.Equal(expected.GetProperty("fxTreatment").GetString(), ToFixture(actual.Treatment));
        Assert.Equal(expected.GetProperty("fxRateTimingDefault").GetString(), ToFixture(actual.RateTiming));
        Assert.Equal(expected.GetProperty("closingRevalue").GetBoolean(), actual.ClosingRevalue);
        Assert.Equal(expected.GetProperty("vatFxMethodOverride").GetString(), ToFixture(actual.VatMethod));
    }

    private static AppPurpose ParsePurpose(string value) => value switch
    {
        "business" => AppPurpose.Business,
        "personal" => AppPurpose.Personal,
        _ => throw new InvalidDataException($"Unknown purpose '{value}'."),
    };

    private static AccountKind ParseKind(string value) => value switch
    {
        "asset" => AccountKind.Asset,
        "liability" => AccountKind.Liability,
        "equity" => AccountKind.Equity,
        "income" => AccountKind.Income,
        "expense" => AccountKind.Expense,
        _ => throw new InvalidDataException($"Unknown account kind '{value}'."),
    };

    private static FxTreatment ParseTreatment(string value) => value switch
    {
        "monetary" => FxTreatment.Monetary,
        "historical" => FxTreatment.Historical,
        "current-value" => FxTreatment.CurrentValue,
        _ => throw new InvalidDataException($"Unknown treatment '{value}'."),
    };

    private static FxRateTiming ParseTiming(string value) => value switch
    {
        "transaction-date" => FxRateTiming.TransactionDate,
        "settlement-date" => FxRateTiming.SettlementDate,
        "valuation-date" => FxRateTiming.ValuationDate,
        _ => throw new InvalidDataException($"Unknown timing '{value}'."),
    };

    private static VatFxMethod ParseVat(string value) => value switch
    {
        "space-default" => VatFxMethod.SpaceDefault,
        "daily" => VatFxMethod.Daily,
        "monthly-average" => VatFxMethod.MonthlyAverage,
        _ => throw new InvalidDataException($"Unknown VAT FX method '{value}'."),
    };

    private static string ToFixture(FxTreatment value) => value switch
    {
        FxTreatment.Monetary => "monetary",
        FxTreatment.Historical => "historical",
        FxTreatment.CurrentValue => "current-value",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToFixture(FxRateTiming value) => value switch
    {
        FxRateTiming.TransactionDate => "transaction-date",
        FxRateTiming.SettlementDate => "settlement-date",
        FxRateTiming.ValuationDate => "valuation-date",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToFixture(VatFxMethod value) => value switch
    {
        VatFxMethod.SpaceDefault => "space-default",
        VatFxMethod.Daily => "daily",
        VatFxMethod.MonthlyAverage => "monthly-average",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
