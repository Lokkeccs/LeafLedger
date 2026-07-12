using System.Text.Json;
using System.Globalization;
using LeafLedger.Modules.Ledger.Domain.Periods;
using LeafLedger.Modules.Ledger.Tests.Fixtures;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public sealed class PeriodTransitionGoldenFixtureTests
{
    public static TheoryData<LedgerCorePostingFixture> Cases
    {
        get
        {
            var data = new TheoryData<LedgerCorePostingFixture>();
            foreach (var fixture in LedgerCorePostingFixtureLoader.LoadSelected()
                         .Where(item => item.File.StartsWith("period-lifecycle/period-transitions/", StringComparison.Ordinal)))
            {
                data.Add(fixture);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_old_period_transition_oracle(LedgerCorePostingFixture fixture)
    {
        using var document = JsonDocument.Parse(fixture.Json);
        var input = document.RootElement.GetProperty("input");
        var expected = document.RootElement.GetProperty("expected").GetProperty("value");
        var from = ParseState(input.GetProperty("initialState").GetString()!);
        var to = ParseState(input.GetProperty("newState").GetString()!);

        var result = PeriodLifecycle.CanTransition(from, to);

        var isSameState = from == to && from is PeriodState.Open or PeriodState.Closed;
        var expectedTargetResult = isSameState ? false : expected.GetProperty("result").GetBoolean();
        Assert.Equal(expectedTargetResult, result.IsAllowed);
    }

    [Theory]
    [InlineData(PeriodState.Open, PeriodState.Locked)]
    [InlineData(PeriodState.Closed, PeriodState.Locked)]
    public void Privileged_lock_transition_is_a_target_surface_decision(PeriodState from, PeriodState to)
    {
        Assert.True(PeriodLifecycle.CanTransition(from, to).IsAllowed);
    }

    [Theory]
    [InlineData("2026-01-01", "2026-01-01", "2026-01-02", true)]
    [InlineData("2026-01-02", "2026-01-01", "2026-01-02", false)]
    [InlineData("2026-01-03", "2026-01-01", "2026-01-02", false)]
    public void Period_range_is_half_open(string date, string start, string endExclusive, bool expected)
    {
        var period = new PeriodSnapshot(
            "January 2026",
            DateOnly.Parse(start, CultureInfo.InvariantCulture),
            DateOnly.Parse(endExclusive, CultureInfo.InvariantCulture),
            PeriodState.Open);

        Assert.Equal(expected, PeriodStateResolver.IsDateInPeriod(DateOnly.Parse(date, CultureInfo.InvariantCulture), period));
    }

    private static PeriodState ParseState(string value) => value switch
    {
        "open" => PeriodState.Open,
        "closed" => PeriodState.Closed,
        "locked" => PeriodState.Locked,
        _ => throw new InvalidDataException($"Unknown period state '{value}'."),
    };
}