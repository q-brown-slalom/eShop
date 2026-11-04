using eShop.Ordering.Infrastructure.Usage;

namespace eShop.Ordering.Infrastructure.EntityConfigurations;

class ApiUsageDailyEntityTypeConfiguration
    : IEntityTypeConfiguration<ApiUsageDaily>
{
    public void Configure(EntityTypeBuilder<ApiUsageDaily> usageConfiguration)
    {
        usageConfiguration.ToTable("apiusagedaily");

        usageConfiguration.HasKey(u => new { u.Day, u.Operation });

        usageConfiguration.Property(u => u.Day)
            .HasColumnType("date");

        usageConfiguration.Property(u => u.Operation)
            .HasMaxLength(64)
            .IsRequired();

        usageConfiguration.Property(u => u.Calls)
            .IsRequired();
    }
}
