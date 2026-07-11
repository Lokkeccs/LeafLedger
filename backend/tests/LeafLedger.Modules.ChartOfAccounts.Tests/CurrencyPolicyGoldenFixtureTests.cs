using System.Text.Json;
using LeafLedger.Modules.ChartOfAccounts.Domain.Accounts;
using LeafLedger.Modules.ChartOfAccounts.Domain.CurrencyPolicy;
using LeafLedger.Modules.ChartOfAccounts.Tests.Fixtures;
using LeafLedger.SharedKernel;
using Xunit;

namespace LeafLedger.Modules.ChartOfAccounts.Tests;

public class CurrencyPolicyGoldenFixtureTests
{
    public static TheoryData<LedgerCoreFixture> Cases
    {
        get
        {
            var data = new TheoryData<LedgerCoreFixture>();
            foreach (var fixture in LedgerCoreFixtureLoader.LoadSelected()
                         .Where(item => item.Unit == "assertPostingCurrencyPolicyValid"))
            {
                data.Add(fixture);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_old_currency_policy_fixture(LedgerCoreFixture fixture)
    {
        using var document = JsonDocument.Parse(fixture.Json);
        var input = document.RootElement.GetProperty("input");
        var expected = document.RootElement.GetProperty("expected");
        var ids = new FixtureIds();

        var accounts = input.GetProperty("accounts").EnumerateArray().Select(item =>
            new CurrencyPolicyAccount(
                ids.Get(item.GetProperty("id").GetInt32()),
                item.GetProperty("currency").GetString()!,
                ParseKind(item.GetProperty("type").GetString()!))).ToArray();
        var references = input.GetProperty("references").EnumerateArray().Select(item =>
            new CurrencyPolicyReference(
                ids.Get(item.GetProperty("accountId").GetInt32()),
                item.GetProperty("txCurrency").GetString()!,
                item.TryGetProperty("source", out var source) ? source.GetString() : null)).ToArray();

        var issues = CurrencyPolicyEvaluator.Evaluate(accounts, references);

        if (expected.TryGetProperty("ok", out _))
        {
            Assert.Empty(issues);
            return;
        }

        var expectedError = expected.GetProperty("error");
        Assert.Equal("CurrencyPolicyError", expectedError.GetProperty("type").GetString());
        var expectedIssues = expectedError.GetProperty("issues").EnumerateArray().ToArray();
        Assert.Equal(expectedIssues.Length, issues.Count);
        for (var index = 0; index < expectedIssues.Length; index++)
        {
            var expectedIssue = expectedIssues[index];
            var actual = issues[index];
            Assert.Equal(expectedIssue.GetProperty("accountId").GetInt32(), ids.Reverse(actual.AccountId));
            Assert.Equal(expectedIssue.GetProperty("accountCurrency").GetString(), actual.AccountCurrency);
            Assert.Equal(expectedIssue.GetProperty("txCurrency").GetString(), actual.TransactionCurrency);
            Assert.Equal(expectedIssue.GetProperty("reason").GetString(), actual.Reason);
            Assert.Equal(
                expectedIssue.TryGetProperty("source", out var source) ? source.GetString() : null,
                actual.Source);
        }
    }

    private static AccountKind ParseKind(string value) => value switch
    {
        "asset" => AccountKind.Asset,
        "liability" => AccountKind.Liability,
        "equity" => AccountKind.Equity,
        "income" => AccountKind.Income,
        "expense" => AccountKind.Expense,
        _ => throw new InvalidDataException($"Unknown account kind '{value}'."),
    };

    private sealed class FixtureIds
    {
        private readonly Dictionary<int, Id<AccountTag>> _forward = [];
        private readonly Dictionary<Id<AccountTag>, int> _reverse = [];

        public Id<AccountTag> Get(int value)
        {
            if (_forward.TryGetValue(value, out var id))
            {
                return id;
            }

            id = Id<AccountTag>.New();
            _forward.Add(value, id);
            _reverse.Add(id, value);
            return id;
        }

        public int Reverse(Id<AccountTag> id) => _reverse[id];
    }
}
