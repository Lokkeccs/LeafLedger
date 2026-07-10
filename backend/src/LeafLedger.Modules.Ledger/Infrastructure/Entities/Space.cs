namespace LeafLedger.Modules.Ledger.Infrastructure.Entities;

/// <summary>Tenant boundary. Every business row carries this space's id.</summary>
public sealed class Space
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string BaseCurrency { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
