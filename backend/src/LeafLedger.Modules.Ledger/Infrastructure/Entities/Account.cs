namespace LeafLedger.Modules.Ledger.Infrastructure.Entities;

/// <summary>A ledger account. Validity windows (business accounts) gate posting; the
/// posting rules themselves are ported in P2-WP04 against the P2-WP01 fixtures.</summary>
public sealed class Account
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public Guid GroupId { get; set; }
    public int Code { get; set; }
    public string Name { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string? FxPolicy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
