using LeafLedger.Modules.Ledger.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeafLedger.Modules.Ledger.Infrastructure.Configurations;

internal sealed class AccountGroupConfiguration : IEntityTypeConfiguration<AccountGroup>
{
    public void Configure(EntityTypeBuilder<AccountGroup> builder)
    {
        builder.ToTable("account_groups");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CodeRange).HasColumnType("int4range").IsRequired();
        builder.Property(x => x.Name).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasOne<Space>().WithMany().HasForeignKey(x => x.SpaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountGroup>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.SpaceId);

        // The non-overlapping code-range invariant (EXCLUDE USING gist) is added as raw
        // SQL in the migration — EF cannot model exclusion constraints.
    }
}
