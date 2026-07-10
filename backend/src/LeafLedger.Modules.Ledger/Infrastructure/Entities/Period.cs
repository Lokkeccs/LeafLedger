namespace LeafLedger.Modules.Ledger.Infrastructure.Entities;

/// <summary>Accounting period as a half-open range <c>[start, endExclusive)</c> + state
/// (open/closed/locked), per SharedKernel <c>Period</c> (P1-WP03). Open/close/lock
/// transitions are P2-WP05.</summary>
public sealed class Period
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public string Name { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndExclusive { get; set; }
    public string State { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
