using eShop.BusinessRules.API.Model;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace eShop.BusinessRules.API.Infrastructure.EntityConfigurations;

public class RulesSnapshotRecordEntityTypeConfiguration : IEntityTypeConfiguration<RulesSnapshotRecord>
{
    public void Configure(EntityTypeBuilder<RulesSnapshotRecord> builder)
    {
        builder.ToTable("snapshots");

        builder.HasKey(s => s.Version);
        // Versions are assigned by the publisher, so concurrent publishes can't reuse a number.
        builder.Property(s => s.Version).ValueGeneratedNever();

        builder.Property(s => s.Content).HasColumnType("jsonb").IsRequired();
    }
}
