using System.Text.Json;
using LeafLedger.Modules.Ledger.Domain.Periods;
using LeafLedger.Modules.Ledger.Tests.Fixtures;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public class PeriodStateGoldenFixtureTests
{
    public static TheoryData<LedgerCorePostingFixture> Cases
    {
        get
        {
            var data = new TheoryData<LedgerCorePostingFixture>();
            foreach (var fixture in LedgerCorePostingFixtureLoader.LoadSelected()
                         .Where(item => item.File.StartsWith("period-state/", StringComparison.Ordinal)))
            {
                data.Add(fixture);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_old_period_state_except_approved_no_period_posting_divergence(LedgerCorePostingFixture fixture)
    {
        using var document = JsonDocument.Parse(fixture.Json);
        var input = document.RootElement.GetProperty("input");
        var expected = document.RootElement.GetProperty("expected");
        var date = FixtureJson.Date(input, "txDate");
        var periods = input.GetProperty("periods").EnumerateArray().Select(item =>
            new PeriodSnapshot(
                item.GetProperty("name").GetString()!,
                FixtureJson.Date(item, "startDate"),
                FixtureJson.Date(item, "endDate"),
                ParseState(item.GetProperty("state").GetString()!))).ToArray();

        if (fixture.Unit == "getEffectivePeriodState")
        {
            Assert.Equal(expected.GetProperty("value").GetString(), ToWireValue(PeriodStateResolver.GetEffectivePeriodState(date, periods)));
            return;
        }

        var result = PeriodStateResolver.AssertPostingPeriodOpen(date, periods);
        if (fixture.Id == "ps-no-period-allowed")
        {
            Assert.True(expected.GetProperty("ok").GetBoolean()); // Immutable OLD oracle.
            Assert.False(result.IsOpen); // ADR-0005 target divergence.
            Assert.Equal("posting_period.not_defined", result.Error!.Code);
            Assert.Null(result.ClosedError);
            return;
        }

        if (expected.TryGetProperty("ok", out _))
        {
            Assert.True(result.IsOpen);
            return;
        }

        var expectedError = expected.GetProperty("error");
        Assert.False(result.IsOpen);
        Assert.Equal("PeriodClosedError", expectedError.GetProperty("type").GetString());
        Assert.NotNull(result.ClosedError);
        Assert.Equal(expectedError.GetProperty("periodName").GetString(), result.ClosedError.PeriodName);
        Assert.Equal(expectedError.GetProperty("state").GetString(), ToWireValue(result.ClosedError.State));
        Assert.Equal(FixtureJson.Date(expectedError, "txDate"), result.ClosedError.TxDate);
    }

    private static PeriodState ParseState(string value) => value switch
    {
        "open" => PeriodState.Open,
        "closed" => PeriodState.Closed,
        "locked" => PeriodState.Locked,
        _ => throw new InvalidDataException($"Unknown period state '{value}'."),
    };

    private static string ToWireValue(PeriodState value) => value switch
    {
        PeriodState.Open => "open",
        PeriodState.Closed => "closed",
        PeriodState.Locked => "locked",
        PeriodState.NoPeriodDefined => "no-period-defined",
        _ => throw new InvalidDataException($"Unknown period state '{value}'."),
    };
}
