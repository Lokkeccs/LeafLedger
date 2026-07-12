using LeafLedger.Modules.Ledger.Application.Posting;
using LeafLedger.Modules.Ledger.Infrastructure;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public sealed class IdempotencyHashTests
{
    [Fact]
    public void Semantically_equivalent_requests_have_the_same_hash()
    {
        var account = Guid.NewGuid();
        var first = Command("  Rent  ", [
            new PostJournalLineRequest(account, 100, "chf", 100),
            new PostJournalLineRequest(account, -100, "CHF", -100),
        ]);
        var second = Command("Rent", [
            new PostJournalLineRequest(account, -100, "CHF", -100),
            new PostJournalLineRequest(account, 100, "CHF", 100),
        ]);

        Assert.Equal(IdempotencyStore.Hash(first), IdempotencyStore.Hash(second));
    }

    [Fact]
    public void Material_changes_have_different_hashes()
    {
        var account = Guid.NewGuid();
        var original = Command("Rent", [
            new PostJournalLineRequest(account, 100, "CHF", 100),
            new PostJournalLineRequest(account, -100, "CHF", -100),
        ]);
        var changed = original with { Description = "Utilities" };

        Assert.NotEqual(IdempotencyStore.Hash(original), IdempotencyStore.Hash(changed));
    }

    private static PostJournalEntryCommand Command(string description, IReadOnlyList<PostJournalLineRequest> lines) =>
        new(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 12), description, "reference", lines);
}