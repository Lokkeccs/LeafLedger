using LeafLedger.Modules.Ledger.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeafLedger.Modules.Ledger.Infrastructure.Configurations;

internal sealed class BusinessPartnerConfiguration : IEntityTypeConfiguration<BusinessPartner>
{
    public void Configure(EntityTypeBuilder<BusinessPartner> builder)
    {
        builder.ToTable("business_partners");
        builder.HasKey(partner => partner.Id);
        builder.Property(partner => partner.Name).HasMaxLength(200).IsRequired();
        builder.Property(partner => partner.PartnerNumber).HasMaxLength(100);
        builder.Property(partner => partner.Type).HasMaxLength(32).IsRequired();
        builder.Property(partner => partner.CountryCode).HasMaxLength(2);
        builder.Property(partner => partner.Notes).HasMaxLength(4000);
        builder.Property(partner => partner.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(partner => partner.UpdatedAt).HasDefaultValueSql("now()");
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .IsConcurrencyToken()
            .ValueGeneratedOnAddOrUpdate();
        builder.HasIndex(partner => new { partner.SpaceId, partner.Name }).IsUnique();
        builder.HasIndex(partner => new { partner.SpaceId, partner.PartnerNumber })
            .IsUnique()
            .HasFilter("partner_number IS NOT NULL");
        builder.HasOne<Space>()
            .WithMany()
            .HasForeignKey(partner => partner.SpaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}