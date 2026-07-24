namespace LeafLedger.Modules.Ledger.Infrastructure.Entities;

public sealed class BusinessPartner
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public string Name { get; set; } = null!;
    public string? PartnerNumber { get; set; }
    public string Type { get; set; } = null!;
    public string? CountryCode { get; set; }
    public bool IsActive { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string? Notes { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}