using eShop.BusinessRules.API.Model;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace eShop.BusinessRules.API.Infrastructure.EntityConfigurations;

public class AuditEntryEntityTypeConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("auditentries");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();

        builder.Property(a => a.ActorId).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.RuleTypeId).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Scope).HasMaxLength(64).IsRequired();
        // Stored as text, not jsonb, so the audit keeps exactly what was recorded.
        builder.Property(a => a.BeforeJson).HasColumnType("text");
        builder.Property(a => a.AfterJson).HasColumnType("text");
        builder.Property(a => a.Reason).HasMaxLength(1000);

        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => new { a.RuleTypeId, a.Scope });
    }
}
