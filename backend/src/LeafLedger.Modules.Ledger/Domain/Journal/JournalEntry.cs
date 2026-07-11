using System.Numerics;
using LeafLedger.SharedKernel;

namespace LeafLedger.Modules.Ledger.Domain.Journal;

public sealed class JournalEntryTag : IEntityTag
{
    public static string Prefix => "je";
}

public sealed class JournalEntry
{
    private JournalEntry(
        Id<JournalEntryTag> id,
        Guid spaceId,
        DateOnly entryDate,
        string description,
        string? reference,
        Guid createdBy,
        Id<JournalEntryTag>? reversesEntryId,
        IReadOnlyList<JournalLine> lines)
    {
        Id = id;
        SpaceId = spaceId;
        EntryDate = entryDate;
        Description = description;
        Reference = reference;
        CreatedBy = createdBy;
        ReversesEntryId = reversesEntryId;
        Lines = lines;
        Status = JournalEntryStatus.Posted;
    }

    public Id<JournalEntryTag> Id { get; }

    public Guid SpaceId { get; }

    public DateOnly EntryDate { get; }

    public string Description { get; }

    public string? Reference { get; }

    public Guid CreatedBy { get; }

    public Id<JournalEntryTag>? ReversesEntryId { get; }

    public JournalEntryStatus Status { get; }

    public IReadOnlyList<JournalLine> Lines { get; }

    public static Result<JournalEntry> Create(
        Id<JournalEntryTag> id,
        Guid spaceId,
        DateOnly entryDate,
        string description,
        string? reference,
        Guid createdBy,
        IReadOnlyCollection<JournalLine> lines,
        Id<JournalEntryTag>? reversesEntryId = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var lineArray = lines.ToArray();
        if (lineArray.Length < 2)
        {
            return Result<JournalEntry>.Failure(new DomainError(
                "journal_entry.insufficient_lines",
                "A journal entry requires at least two lines."));
        }

        var baseBalance = lineArray.Aggregate(BigInteger.Zero, (sum, line) => sum + line.BaseAmountMinor);
        if (baseBalance != BigInteger.Zero)
        {
            return Result<JournalEntry>.Failure(new DomainError(
                "journal_entry.unbalanced",
                "Journal entry base amounts must sum to zero."));
        }

        return Result<JournalEntry>.Success(new JournalEntry(
            id,
            spaceId,
            entryDate,
            description,
            reference,
            createdBy,
            reversesEntryId,
            Array.AsReadOnly(lineArray)));
    }

    public Result<JournalEntry> Reverse(
        DateOnly reversalDate,
        Id<JournalEntryTag> newId,
        Guid createdBy)
    {
        var reversedLines = new List<JournalLine>(Lines.Count);
        foreach (var line in Lines)
        {
            var reversedLine = line.Negate();
            if (reversedLine.IsFailure)
            {
                return Result<JournalEntry>.Failure(reversedLine.Error!);
            }

            reversedLines.Add(reversedLine.Value);
        }

        return Create(
            newId,
            SpaceId,
            reversalDate,
            Description,
            Reference,
            createdBy,
            reversedLines,
            Id);
    }
}
