using LeafLedger.Modules.Ledger.Domain.Journal;
using LeafLedger.SharedKernel;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

internal sealed class TestUserTag : IEntityTag
{
    public static string Prefix => "usr";
}

internal static class JournalEntryTestData
{
    public static JournalLine Line(
        long amountMinor,
        long baseAmountMinor,
        CurrencyCode? currency = null,
        IReadOnlyCollection<LineAttribution>? attributions = null)
    {
        var result = JournalLine.Create(
            Guid.NewGuid(),
            amountMinor,
            currency ?? CurrencyCode.Chf,
            baseAmountMinor,
            attributions: attributions);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    public static JournalEntry Entry(params JournalLine[] lines)
    {
        var result = JournalEntry.Create(
            Id<JournalEntryTag>.New(),
            Guid.NewGuid(),
            new DateOnly(2026, 7, 11),
            "Test posting",
            "TEST-1",
            Id<TestUserTag>.New().ToStorage(),
            lines);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }
}
