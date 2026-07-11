using LeafLedger.Modules.ChartOfAccounts.Tests.Fixtures;
using Xunit;

namespace LeafLedger.Modules.ChartOfAccounts.Tests;

public class FixtureSelectionTests
{
    [Fact]
    public void Selects_exactly_the_22_chart_of_accounts_fixtures()
    {
        var fixtures = LedgerCoreFixtureLoader.LoadSelected();

        Assert.Equal(22, fixtures.Count);
        Assert.Equal(11, fixtures.Count(fixture => fixture.File.StartsWith("currency-policy/", StringComparison.Ordinal)));
        Assert.Equal(11, fixtures.Count(fixture => fixture.File.StartsWith("fx-metadata/", StringComparison.Ordinal)));
    }
}
