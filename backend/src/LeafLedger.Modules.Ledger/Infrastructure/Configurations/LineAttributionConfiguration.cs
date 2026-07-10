using LeafLedger.Modules.Ledger.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeafLedger.Modules.Ledger.Infrastructure.Configurations;

internal sealed class LineAttributionConfiguration : IEntityTypeConfiguration<LineAttribution>
{
    public void Configure(EntityTypeBuilder<LineAttribution> builder)
    {
        builder.ToTable(
            "line_attributions",
            t => t.HasCheckConstraint(
                "ck_line_attributions_share_permille",
                "share_permille BETWEEN 1 AND 1000"));
        builder.HasKey(x => x.Id);
        builder.HasOne<JournalLine>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Space>().WithMany().HasForeignKey(x => x.SpaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.LineId);
        builder.HasIndex(x => x.SpaceId);
    }
}
