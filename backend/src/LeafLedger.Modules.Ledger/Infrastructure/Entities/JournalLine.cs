namespace LeafLedger.Modules.Ledger.Infrastructure.Entities;

/// <summary>
/// A single posting line. Money is <c>bigint</c> minor units (signed: +debit / −credit) +
/// ISO currency; never a float. The per-entry base-amount sum = 0 invariant is enforced
/// by a deferred DB constraint trigger. <c>VatCodeId</c>/<c>BusinessPartnerId</c>/
/// <c>ProjectId</c> are nullable, un-FK'd placeholders until their owning WPs.
/// </summary>
public sealed class JournalLine
{
    public Guid Id { get; set; }
    public Guid EntryId { get; set; }
    public Guid SpaceId { get; set; }
    public Guid AccountId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = null!;
    public long BaseAmountMinor { get; set; }
    public decimal? FxRate { get; set; }
    public Guid? VatCodeId { get; set; }
    public Guid? BusinessPartnerId { get; set; }
    public Guid? ProjectId { get; set; }
}
