using LeafLedger.SharedKernel;
using Xunit;

namespace LeafLedger.SharedKernel.Tests;

public class PeriodTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Feb1 = new(2026, 2, 1);
    private static readonly DateOnly Mar1 = new(2026, 3, 1);

    [Fact]
    public void Contains_is_start_inclusive_and_end_exclusive()
    {
        var period = Period.Create(Jan1, Feb1);

        Assert.True(period.Contains(Jan1));                       // start inclusive
        Assert.True(period.Contains(new DateOnly(2026, 1, 31)));  // interior
        Assert.False(period.Contains(Feb1));                      // end exclusive
        Assert.False(period.Contains(new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void Create_rejects_empty_or_inverted_ranges()
    {
        Assert.Throws<ArgumentException>(() => Period.Create(Feb1, Jan1));
        Assert.Throws<ArgumentException>(() => Period.Create(Jan1, Jan1));
    }

    [Fact]
    public void Defaults_to_open_status()
    {
        Assert.Equal(PeriodStatus.Open, Period.Create(Jan1, Feb1).Status);
        Assert.Equal(PeriodStatus.Closed, Period.Create(Jan1, Feb1, PeriodStatus.Closed).Status);
    }

    [Fact]
    public void Ordering_is_by_start_then_end()
    {
        var jan = Period.Create(Jan1, Feb1);
        var febShort = Period.Create(Feb1, Mar1);
        var janLong = Period.Create(Jan1, Mar1);

        Assert.True(jan < febShort);
        Assert.True(jan < janLong);         // same start, earlier end sorts first
        Assert.True(febShort > janLong);
    }

    [Fact]
    public void Equality_is_by_value()
    {
        Assert.Equal(Period.Create(Jan1, Feb1), Period.Create(Jan1, Feb1));
        Assert.NotEqual(Period.Create(Jan1, Feb1), Period.Create(Jan1, Feb1, PeriodStatus.Closed));
    }
}
