using LeafLedger.Modules.Ledger.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeafLedger.Modules.Ledger.Infrastructure.Configurations;

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Role).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasOne<Space>().WithMany().HasForeignKey(x => x.SpaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.SpaceId);
    }
}
