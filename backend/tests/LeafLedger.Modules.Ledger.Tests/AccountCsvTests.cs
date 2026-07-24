using LeafLedger.Modules.Ledger.Application.Accounts;
using LeafLedger.Modules.Ledger.Infrastructure;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public sealed class AccountCsvTests
{
    [Fact]
    public void Accounts_round_trip_rfc4180_fields_and_optional_values()
    {
        var rows = new[]
        {
            new AccountImportRow(
                "asset",
                1100,
                "Cash, \"operating\"\naccount",
                "CHF",
                "Current accounts",
                true,
                new DateOnly(2026, 1, 2),
                null,
                "policy\"default"),
        };

        var csv = AccountCsv.WriteAccounts(rows);
        var parsed = AccountCsv.ReadAccounts("\uFEFF" + csv.Replace("\r\n", "\n", StringComparison.Ordinal));

        Assert.Equal(rows, parsed);
        Assert.StartsWith("kind,code,name,currency,group,isActive,validFrom,validTo,fxPolicy\r\n", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void Groups_round_trip_empty_parent_and_crlf()
    {
        var rows = new[]
        {
            new GroupImportRow("Assets", 1000, 1999, null, null),
            new GroupImportRow("Cash", 1100, 1199, "Assets", "fx-policy"),
        };

        var parsed = AccountCsv.ReadGroups(AccountCsv.WriteGroups(rows));

        Assert.Equal(rows, parsed);
    }

    [Fact]
    public void Unknown_header_is_ignored_and_preserved_as_warning()
    {
        const string csv = "kind,code,name,currency,group,isActive,validFrom,validTo,fxPolicy,ownerEmail\r\n" +
            "asset,1100,Cash,CHF,Assets,TRUE,,,owner@example.com\r\n";

        var parsed = AccountCsv.ReadAccountsWithWarnings(csv);

        Assert.Single(parsed.Rows);
        Assert.Contains(parsed.Rows[0].Warnings, warning => warning.Contains("ownerEmail", StringComparison.Ordinal));
        Assert.Equal("Cash", parsed.Rows[0].Value.Name);
    }
}