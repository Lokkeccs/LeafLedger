using System.Text.Json;
using LeafLedger.Modules.Ledger.Domain.PostingValidity;
using LeafLedger.Modules.Ledger.Tests.Fixtures;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public class PostingAccountsGoldenFixtureTests
{
    public static TheoryData<LedgerCorePostingFixture> Cases => FixtureCases.ForUnit("assertPostingAccountsValid");

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_old_account_validity_fixture(LedgerCorePostingFixture fixture)
    {
        using var document = JsonDocument.Parse(fixture.Json);
        var input = document.RootElement.GetProperty("input");
        var ids = new FixtureIds();
        var accounts = input.GetProperty("accounts").EnumerateArray().Select(item =>
            new AccountReference(
                ids.Get(item.GetProperty("id").GetInt64()),
                item.GetProperty("isActive").GetBoolean(),
                FixtureJson.OptionalDate(item, "validFrom"),
                FixtureJson.OptionalDate(item, "validTo"))).ToArray();
        var references = input.GetProperty("references").EnumerateArray().Select(item =>
            new PostingReference(
                ids.Get(item.GetProperty("accountId").GetInt64()),
                FixtureJson.Date(item, "txDate"),
                FixtureJson.OptionalString(item, "source"))).ToArray();

        var error = PostingValidityEvaluator.AssertPostingAccountsValid(
            ParsePurpose(input.GetProperty("purpose").GetString()!), accounts, references);

        GoldenAssertions.MatchPostingError(document.RootElement.GetProperty("expected"), error, ids);
    }

    internal static PostingPurpose ParsePurpose(string value) => value switch
    {
        "business" => PostingPurpose.Business,
        "personal" => PostingPurpose.Personal,
        _ => throw new InvalidDataException($"Unknown posting purpose '{value}'."),
    };
}
