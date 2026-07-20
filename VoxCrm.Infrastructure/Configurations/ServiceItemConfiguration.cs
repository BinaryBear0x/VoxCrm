using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Infrastructure.Configurations;

public sealed class ServiceItemConfiguration : IEntityTypeConfiguration<ServiceItem>
{
    public void Configure(EntityTypeBuilder<ServiceItem> builder)
    {
        builder.HasIndex(item => new { item.ClinicID, item.Name })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
        builder.ToTable(table => table.HasCheckConstraint("CK_ServiceItems_Price_NonNegative", "\"Price\" >= 0"));
    }
}
