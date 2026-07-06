using LeafLedger.SharedKernel;
using Xunit;

namespace LeafLedger.SharedKernel.Tests;

file sealed class JournalEntryTag : IEntityTag
{
    public static string Prefix => "je";
}

file sealed class AccountTag : IEntityTag
{
    public static string Prefix => "acc";
}

public class IdTests
{
    [Fact]
    public void Ulid_uuid_round_trips_losslessly()
    {
        for (var i = 0; i < 10_000; i++)
        {
            var id = Id<JournalEntryTag>.New();
            var restored = Id<JournalEntryTag>.FromStorage(id.ToStorage());
            Assert.Equal(id, restored);
        }
    }

    [Fact]
    public void Storage_uuid_byte_order_preserves_ulid_order()
    {
        var ids = new List<Id<JournalEntryTag>>();
        for (var i = 0; i < 5_000; i++)
        {
            ids.Add(Id<JournalEntryTag>.New());
        }

        // Sort by the ULID (byte-wise, MSB-first) ...
        ids.Sort((a, b) => a.Value.CompareTo(b.Value));

        // ... then the storage uuid bytes must be non-decreasing too, i.e. Postgres
        // uuid ordering matches ULID ordering.
        for (var i = 1; i < ids.Count; i++)
        {
            var previous = ids[i - 1].ToStorage().ToByteArray(bigEndian: true);
            var current = ids[i].ToStorage().ToByteArray(bigEndian: true);
            Assert.True(LexicographicCompare(previous, current) <= 0);
        }
    }

    [Fact]
    public void Storage_form_carries_no_prefix_but_boundary_form_does()
    {
        var id = Id<JournalEntryTag>.New();

        Assert.StartsWith("je_", id.ToBoundaryString(), StringComparison.Ordinal);
        Assert.DoesNotContain("je_", id.ToStorage().ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseBoundary_round_trips_a_correct_prefixed_string()
    {
        var id = Id<JournalEntryTag>.New();
        var text = id.ToBoundaryString();

        var parsed = Id<JournalEntryTag>.ParseBoundary(text);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(id, parsed.Value);
        Assert.Equal(text, parsed.Value.ToBoundaryString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("acc_01ARZ3NDEKTSV4RRFFQ69G5FAV")] // wrong prefix
    [InlineData("01ARZ3NDEKTSV4RRFFQ69G5FAV")]     // no prefix
    [InlineData("je_not-a-ulid")]                   // malformed body
    public void ParseBoundary_rejects_invalid_input(string? text)
    {
        Assert.True(Id<JournalEntryTag>.ParseBoundary(text).IsFailure);
    }

    private static int LexicographicCompare(byte[] left, byte[] right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            var comparison = left[i].CompareTo(right[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}
