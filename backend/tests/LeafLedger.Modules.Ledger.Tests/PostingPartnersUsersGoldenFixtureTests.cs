using System.Text.Json;
using LeafLedger.Modules.Ledger.Domain.PostingValidity;
using LeafLedger.Modules.Ledger.Tests.Fixtures;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public class PostingPartnersUsersGoldenFixtureTests
{
    public static TheoryData<LedgerCorePostingFixture> Cases
    {
        get
        {
            var data = new TheoryData<LedgerCorePostingFixture>();
            foreach (var fixture in LedgerCorePostingFixtureLoader.LoadSelected().Where(item =>
                         item.Unit is "assertPostingBusinessPartnersValid" or "assertPostingUsersValid"))
            {
                data.Add(fixture);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_old_timebound_reference_fixture(LedgerCorePostingFixture fixture)
    {
        using var document = JsonDocument.Parse(fixture.Json);
        var input = document.RootElement.GetProperty("input");
        var ids = new FixtureIds();
        var isPartner = fixture.Unit == "assertPostingBusinessPartnersValid";
        var catalogName = isPartner ? "businessPartners" : "users";
        var idName = isPartner ? "businessPartnerId" : "userId";
        var catalog = input.GetProperty(catalogName).EnumerateArray().Select(item =>
            new TimeboundReference(
                ids.Get(item.GetProperty("id").GetInt64()),
                !item.TryGetProperty("isActive", out var isActive) || isActive.GetBoolean(),
                FixtureJson.OptionalDate(item, "validFrom"),
                FixtureJson.OptionalDate(item, "validTo"))).ToArray();
        var references = input.GetProperty("references").EnumerateArray().Select(item =>
            new PostingReference(
                ids.Get(item.GetProperty(idName).GetInt64()),
                FixtureJson.Date(item, "txDate"),
                FixtureJson.OptionalString(item, "source"))).ToArray();

        var purpose = PostingAccountsGoldenFixtureTests.ParsePurpose(input.GetProperty("purpose").GetString()!);
        var error = isPartner
            ? PostingValidityEvaluator.AssertPostingBusinessPartnersValid(purpose, catalog, references)
            : PostingValidityEvaluator.AssertPostingUsersValid(purpose, catalog, references);

        GoldenAssertions.MatchPostingError(document.RootElement.GetProperty("expected"), error, ids);
    }
}
