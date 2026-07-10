namespace LeafLedger.Modules.Ledger.Infrastructure.Entities;

/// <summary>Posted journal entry header. Append-only (no UPDATE/DELETE grants);
/// corrections are reversals linked via <see cref="ReversesEntryId"/>. Reversal
/// logic + exact-integer balance is P2-WP04/WP05.</summary>
public sealed class JournalEntry
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public long EntryNo { get; set; }
    public DateOnly Date { get; set; }
    public string Status { get; set; } = null!;
    public string? Description { get; set; }
    public string? Reference { get; set; }
    public Guid? ReversesEntryId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
