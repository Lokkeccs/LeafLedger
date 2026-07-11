using LeafLedger.Modules.Ledger.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeafLedger.Modules.Ledger.Infrastructure.Configurations;

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("journal_entries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasOne<Space>().WithMany().HasForeignKey(x => x.SpaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.ReversesEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.SpaceId);
        builder.HasIndex(x => new { x.SpaceId, x.EntryNo }).IsUnique();
    }
}
