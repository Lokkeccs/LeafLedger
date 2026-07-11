using LeafLedger.Modules.Ledger.Infrastructure;
using System.Linq;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public sealed class IntegrityHasherTests
{
    [Fact]
    public void Hash_is_independent_of_input_order()
    {
        var rows = new[]
        {
            new IntegrityBalanceRow(Guid.Parse("00000000-0000-0000-0000-000000000002"), 200, 12_345),
            new IntegrityBalanceRow(Guid.Parse("00000000-0000-0000-0000-000000000001"), 100, -12_345),
        };

        var reordered = rows.AsEnumerable().Reverse().ToArray();

        Assert.Equal(
            IntegrityHasher.Compute(Guid.Parse("00000000-0000-0000-0000-000000000010"), rows),
            IntegrityHasher.Compute(Guid.Parse("00000000-0000-0000-0000-000000000010"), reordered));
    }

    [Fact]
    public void Hash_changes_when_a_balance_changes()
    {
        var rows = new[]
        {
            new IntegrityBalanceRow(Guid.Parse("00000000-0000-0000-0000-000000000001"), 100, 12_345),
        };

        var changed = new[]
        {
            new IntegrityBalanceRow(rows[0].AccountId, rows[0].AccountCode, 12_346),
        };

        Assert.NotEqual(
            IntegrityHasher.Compute(Guid.Parse("00000000-0000-0000-0000-000000000010"), rows),
            IntegrityHasher.Compute(Guid.Parse("00000000-0000-0000-0000-000000000010"), changed));
    }

    [Fact]
    public void Empty_input_has_a_stable_hash()
    {
        var spaceId = Guid.Parse("00000000-0000-0000-0000-000000000010");

        Assert.Equal(
            IntegrityHasher.Compute(spaceId, Array.Empty<IntegrityBalanceRow>()),
            IntegrityHasher.Compute(spaceId, Array.Empty<IntegrityBalanceRow>()));
    }

    [Fact]
    public void Hash_does_not_depend_on_current_culture()
    {
        var rows = new[]
        {
            new IntegrityBalanceRow(Guid.Parse("00000000-0000-0000-0000-000000000001"), 100, -12_345),
        };
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-CH");
            var swissHash = IntegrityHasher.Compute(Guid.Parse("00000000-0000-0000-0000-000000000010"), rows);
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            var usHash = IntegrityHasher.Compute(Guid.Parse("00000000-0000-0000-0000-000000000010"), rows);

            Assert.Equal(swissHash, usHash);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }
}