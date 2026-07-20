using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Infrastructure.Configurations;

public sealed class ClinicConfiguration : IEntityTypeConfiguration<Clinic>
{
    public void Configure(EntityTypeBuilder<Clinic> builder)
    {
        builder.HasIndex(clinic => clinic.Slug).IsUnique();
        builder.Property(clinic => clinic.TimeZoneId).HasMaxLength(100);
    }
}
