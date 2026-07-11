using LeafLedger.Modules.Ledger.Tests.Fixtures;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public class FixtureSelectionTests
{
    [Fact]
    public void Selects_exactly_the_37_ledger_posting_fixtures()
    {
        var fixtures = LedgerCorePostingFixtureLoader.LoadSelected();

        Assert.Equal(37, fixtures.Count);
        Assert.Equal(10, CountFolder(fixtures, "posting-accounts/"));
        Assert.Equal(7, CountFolder(fixtures, "posting-business-partners/"));
        Assert.Equal(7, CountFolder(fixtures, "posting-users/"));
        Assert.Equal(5, CountFolder(fixtures, "posting-projects/"));
        Assert.Equal(8, CountFolder(fixtures, "period-state/"));
    }

    private static int CountFolder(IEnumerable<LedgerCorePostingFixture> fixtures, string prefix) =>
        fixtures.Count(fixture => fixture.File.StartsWith(prefix, StringComparison.Ordinal));
}
