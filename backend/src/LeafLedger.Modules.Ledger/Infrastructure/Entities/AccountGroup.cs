using NpgsqlTypes;

namespace LeafLedger.Modules.Ledger.Infrastructure.Entities;

/// <summary>
/// Account group with an <c>int4range</c> code range. Ranges may not overlap within a
/// space (GiST exclusion constraint). FX-policy resolution is P2-WP03.
/// </summary>
public sealed class AccountGroup
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public NpgsqlRange<int> CodeRange { get; set; }
    public string Name { get; set; } = null!;
    public Guid? ParentId { get; set; }
    public string? FxPolicy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
