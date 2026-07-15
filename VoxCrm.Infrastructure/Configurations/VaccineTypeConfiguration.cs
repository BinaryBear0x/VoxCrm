using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Infrastructure.Configurations;

public sealed class VaccineTypeConfiguration : IEntityTypeConfiguration<VaccineType>
{
    public void Configure(EntityTypeBuilder<VaccineType> builder)
    {
        builder.HasIndex(vaccine => new { vaccine.ClinicID, vaccine.Name })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_VaccineTypes_ValidityDays", "\"ValidityDays\" BETWEEN 1 AND 3650");
            table.HasCheckConstraint(
                "CK_VaccineTypes_ReminderDaysBefore",
                "\"ReminderDaysBefore\" >= 0 AND \"ReminderDaysBefore\" <= \"ValidityDays\"");
        });
    }
}
