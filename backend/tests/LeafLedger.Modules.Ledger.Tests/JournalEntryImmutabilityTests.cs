using System.Reflection;
using LeafLedger.Modules.Ledger.Domain.Journal;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public class JournalEntryImmutabilityTests
{
    [Fact]
    public void Aggregate_exposes_no_public_property_setters_or_mutation_methods()
    {
        var properties = typeof(JournalEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.All(properties, property => Assert.Null(property.SetMethod));

        var allowedMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(JournalEntry.Reverse),
            nameof(object.Equals),
            nameof(object.GetHashCode),
            nameof(object.GetType),
            nameof(object.ToString),
        };
        var unexpected = typeof(JournalEntry).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => !method.IsSpecialName && !allowedMethods.Contains(method.Name))
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unexpected);
    }

    [Fact]
    public void Input_line_collection_cannot_mutate_created_entry()
    {
        var lines = new List<JournalLine>
        {
            JournalEntryTestData.Line(100, 100),
            JournalEntryTestData.Line(-100, -100),
        };
        var entry = JournalEntryTestData.Entry(lines.ToArray());

        lines.Clear();

        Assert.Equal(2, entry.Lines.Count);
        Assert.False(entry.Lines is JournalLine[]);
    }
}
