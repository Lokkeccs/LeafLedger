namespace LeafLedger.Modules.Ledger.Infrastructure.Entities;

/// <summary>A user's membership + typed role within a space. AuthZ pipeline is P2-WP06.</summary>
public sealed class Membership
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
