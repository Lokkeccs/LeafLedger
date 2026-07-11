using System.Reflection;
using LeafLedger.Modules.Ledger.Domain.Journal;
using LeafLedger.SharedKernel;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public class JournalEntryBalanceTests
{
    [Fact]
    public void Creates_balanced_same_currency_entry()
    {
        var result = Create(
            JournalEntryTestData.Line(1250, 1250),
            JournalEntryTestData.Line(-1250, -1250));

        Assert.True(result.IsSuccess);
        Assert.Equal(JournalEntryStatus.Posted, result.Value.Status);
        Assert.Equal(2, result.Value.Lines.Count);
    }

    [Fact]
    public void Creates_balanced_multi_currency_entry_when_base_amounts_net_to_zero()
    {
        var result = Create(
            JournalEntryTestData.Line(10000, 9000, CurrencyCode.Usd),
            JournalEntryTestData.Line(-8500, -9000, CurrencyCode.Eur));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Creates_balanced_entry_at_long_boundaries_without_overflow()
    {
        var result = Create(
            JournalEntryTestData.Line(long.MaxValue, long.MaxValue),
            JournalEntryTestData.Line(1, 1),
            JournalEntryTestData.Line(long.MinValue, long.MinValue));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Rejects_unbalanced_entry()
    {
        var result = Create(
            JournalEntryTestData.Line(1000, 1000),
            JournalEntryTestData.Line(-999, -999));

        Assert.True(result.IsFailure);
        Assert.Equal("journal_entry.unbalanced", result.Error!.Code);
    }

    [Fact]
    public void Rejects_entry_with_fewer_than_two_lines()
    {
        var result = Create(JournalEntryTestData.Line(1000, 0));

        Assert.True(result.IsFailure);
        Assert.Equal("journal_entry.insufficient_lines", result.Error!.Code);
    }

    [Theory]
    [InlineData(typeof(JournalEntry))]
    [InlineData(typeof(JournalLine))]
    [InlineData(typeof(LineAttribution))]
    public void Public_members_expose_no_floating_point_or_decimal(Type type)
    {
        var forbidden = new[] { typeof(float), typeof(double), typeof(decimal) };
        var exposedTypes = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(property => property.PropertyType)
            .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)))
            .Select(Unwrap)
            .ToArray();

        Assert.DoesNotContain(exposedTypes, exposed => forbidden.Contains(exposed));
    }

    private static Result<JournalEntry> Create(params JournalLine[] lines) => JournalEntry.Create(
        Id<JournalEntryTag>.New(),
        Guid.NewGuid(),
        new DateOnly(2026, 7, 11),
        "Test",
        null,
        Guid.NewGuid(),
        lines);

    private static Type Unwrap(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType()!;
        }

        if (type.IsGenericType)
        {
            return type.GetGenericArguments().Select(Unwrap).FirstOrDefault(candidate =>
                candidate == typeof(float) || candidate == typeof(double) || candidate == typeof(decimal)) ?? type;
        }

        return type;
    }
}
