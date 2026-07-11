namespace LeafLedger.Modules.Ledger.Infrastructure.Entities;

/// <summary>Splits a line across users in per-mille shares. The per-line sum = 1000‰
/// invariant is a domain rule (P2-WP04); only the per-row 1..1000 CHECK is DB-side here.</summary>
public sealed class LineAttribution
{
    public Guid Id { get; set; }
    public Guid LineId { get; set; }
    public Guid SpaceId { get; set; }
    public Guid UserId { get; set; }
    public int SharePermille { get; set; }
}
