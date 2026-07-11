using LeafLedger.Modules.Ledger.Domain.Journal;
using LeafLedger.SharedKernel;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public class LineAttributionTests
{
    [Fact]
    public void Accepts_no_attributions()
    {
        var result = JournalLine.Create(Guid.NewGuid(), 100, CurrencyCode.Chf, 100);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Attributions);
    }

    [Fact]
    public void Accepts_attributions_summing_exactly_1000_permille()
    {
        var result = JournalLine.Create(
            Guid.NewGuid(),
            100,
            CurrencyCode.Chf,
            100,
            attributions:
            [
                new LineAttribution(Guid.NewGuid(), 600),
                new LineAttribution(Guid.NewGuid(), 400),
            ]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Rejects_attribution_sum_other_than_1000_permille()
    {
        var result = JournalLine.Create(
            Guid.NewGuid(),
            100,
            CurrencyCode.Chf,
            100,
            attributions: [new LineAttribution(Guid.NewGuid(), 999)]);

        Assert.True(result.IsFailure);
        Assert.Equal("line_attribution.share_sum_invalid", result.Error!.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Rejects_share_outside_inclusive_range(int sharePermille)
    {
        var result = JournalLine.Create(
            Guid.NewGuid(),
            100,
            CurrencyCode.Chf,
            100,
            attributions: [new LineAttribution(Guid.NewGuid(), sharePermille)]);

        Assert.True(result.IsFailure);
        Assert.Equal("line_attribution.share_out_of_range", result.Error!.Code);
    }

    [Fact]
    public void Rejects_duplicate_user_attributions()
    {
        var userId = Guid.NewGuid();
        var result = JournalLine.Create(
            Guid.NewGuid(),
            100,
            CurrencyCode.Chf,
            100,
            attributions:
            [
                new LineAttribution(userId, 500),
                new LineAttribution(userId, 500),
            ]);

        Assert.True(result.IsFailure);
        Assert.Equal("line_attribution.duplicate_user", result.Error!.Code);
    }
}
