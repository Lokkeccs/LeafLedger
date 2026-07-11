using LeafLedger.Modules.Ledger.Domain.Journal;
using LeafLedger.SharedKernel;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public class JournalEntryReversalTests
{
    [Fact]
    public void Reversal_is_new_balanced_entry_with_negated_lines_and_link()
    {
        var accountId = Guid.NewGuid();
        var fxRateMetadataId = Guid.NewGuid();
        var vatCodeId = Guid.NewGuid();
        var businessPartnerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var attributions = new[] { new LineAttribution(Guid.NewGuid(), 1000) };
        var richLine = JournalLine.Create(
            accountId,
            10000,
            CurrencyCode.Usd,
            9000,
            fxRateMetadataId,
            vatCodeId,
            businessPartnerId,
            projectId,
            attributions);
        Assert.True(richLine.IsSuccess);
        var original = JournalEntryTestData.Entry(
            richLine.Value,
            JournalEntryTestData.Line(-9000, -9000, CurrencyCode.Chf));
        var originalAmounts = original.Lines.Select(line => (line.AmountMinor, line.BaseAmountMinor)).ToArray();
        var reversalId = Id<JournalEntryTag>.New();
        var reversalDate = new DateOnly(2026, 7, 12);

        var result = original.Reverse(reversalDate, reversalId, Guid.NewGuid());

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(reversalId, result.Value.Id);
        Assert.Equal(reversalDate, result.Value.EntryDate);
        Assert.Equal(original.Id, result.Value.ReversesEntryId);
        Assert.Equal(original.Reference, result.Value.Reference);
        Assert.Equal(original.Lines.Count, result.Value.Lines.Count);
        for (var index = 0; index < original.Lines.Count; index++)
        {
            Assert.Equal(-original.Lines[index].AmountMinor, result.Value.Lines[index].AmountMinor);
            Assert.Equal(-original.Lines[index].BaseAmountMinor, result.Value.Lines[index].BaseAmountMinor);
        }

        var reversedRichLine = result.Value.Lines[0];
        Assert.Equal(accountId, reversedRichLine.AccountId);
        Assert.Equal(CurrencyCode.Usd, reversedRichLine.Currency);
        Assert.Equal(fxRateMetadataId, reversedRichLine.FxRateMetadataId);
        Assert.Equal(vatCodeId, reversedRichLine.VatCodeId);
        Assert.Equal(businessPartnerId, reversedRichLine.BusinessPartnerId);
        Assert.Equal(projectId, reversedRichLine.ProjectId);
        Assert.Equal(attributions, reversedRichLine.Attributions);

        Assert.Equal(originalAmounts, original.Lines.Select(line => (line.AmountMinor, line.BaseAmountMinor)).ToArray());
    }

    [Fact]
    public void Reversing_a_reversal_is_allowed()
    {
        var original = JournalEntryTestData.Entry(
            JournalEntryTestData.Line(100, 100),
            JournalEntryTestData.Line(-100, -100));
        var first = original.Reverse(new DateOnly(2026, 7, 12), Id<JournalEntryTag>.New(), Guid.NewGuid());

        var second = first.Value.Reverse(new DateOnly(2026, 7, 13), Id<JournalEntryTag>.New(), Guid.NewGuid());

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Id, second.Value.ReversesEntryId);
        Assert.Equal(original.Lines.Select(line => line.BaseAmountMinor), second.Value.Lines.Select(line => line.BaseAmountMinor));
    }

    [Fact]
    public void Reverse_requires_explicit_date_parameter()
    {
        var methods = typeof(JournalEntry).GetMethods().Where(method => method.Name == nameof(JournalEntry.Reverse)).ToArray();

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            var date = Assert.Single(method.GetParameters(), parameter => parameter.ParameterType == typeof(DateOnly));
            Assert.False(date.HasDefaultValue);
        });
    }
}
