using eShop.BusinessRules.API.Model;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace eShop.BusinessRules.API.Infrastructure.EntityConfigurations;

public class RuleVersionEntityTypeConfiguration : IEntityTypeConfiguration<RuleVersion>
{
    public void Configure(EntityTypeBuilder<RuleVersion> builder)
    {
        builder.ToTable("ruleversions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.RuleTypeId).HasMaxLength(64).IsRequired();
        builder.Property(v => v.Scope).HasMaxLength(64).IsRequired();
        builder.Property(v => v.Parameters).HasColumnType("jsonb").IsRequired();
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(v => v.CreatedBy).HasMaxLength(200).IsRequired();
        builder.Property(v => v.PublishedBy).HasMaxLength(200);

        builder.HasIndex(v => new { v.RuleTypeId, v.Scope, v.VersionNumber }).IsUnique();
    }
}
