namespace LeafLedger.Modules.Ledger.Infrastructure.Entities;

/// <summary>Trigger-maintained audit trail for mutable tables (who/when/what/before/after).
/// Journal tables are their own audit record (append-only) and are not audited here.</summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; set; }
    public Guid SpaceId { get; set; }
    public string TableName { get; set; } = null!;
    public Guid RowId { get; set; }
    public string Action { get; set; } = null!;
    public string? Actor { get; set; }
    public DateTimeOffset At { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
}
