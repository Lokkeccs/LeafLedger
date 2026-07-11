using System.Text.Json;
using LeafLedger.Modules.Ledger.Domain.PostingValidity;
using LeafLedger.Modules.Ledger.Tests.Fixtures;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public class PostingProjectsGoldenFixtureTests
{
    public static TheoryData<LedgerCorePostingFixture> Cases => FixtureCases.ForUnit("assertPostingProjectsValid");

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_old_project_validity_fixture(LedgerCorePostingFixture fixture)
    {
        using var document = JsonDocument.Parse(fixture.Json);
        var input = document.RootElement.GetProperty("input");
        var ids = new FixtureIds();
        var projects = input.GetProperty("projects").EnumerateArray().Select(item =>
            new ProjectReference(
                ids.Get(item.GetProperty("id").GetInt64()),
                FixtureJson.OptionalDate(item, "startDate"),
                FixtureJson.OptionalDate(item, "endDate"))).ToArray();
        var references = input.GetProperty("references").EnumerateArray().Select(item =>
            new PostingReference(
                ids.Get(item.GetProperty("projectId").GetInt64()),
                FixtureJson.Date(item, "txDate"),
                FixtureJson.OptionalString(item, "source"))).ToArray();

        var error = PostingValidityEvaluator.AssertPostingProjectsValid(projects, references);

        GoldenAssertions.MatchPostingError(document.RootElement.GetProperty("expected"), error, ids);
    }
}
