using LeafLedger.Modules.Ledger.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeafLedger.Modules.Ledger.Infrastructure.Configurations;

internal sealed class PeriodConfiguration : IEntityTypeConfiguration<Period>
{
    public void Configure(EntityTypeBuilder<Period> builder)
    {
        builder.ToTable("periods");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired();
        builder.Property(x => x.State).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasOne<Space>().WithMany().HasForeignKey(x => x.SpaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.SpaceId);
    }
}
