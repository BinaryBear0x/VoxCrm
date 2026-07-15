using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Infrastructure.Configurations;

public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.HasIndex(patient => new { patient.ClinicID, patient.MicrochipNumber })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE AND \"MicrochipNumber\" IS NOT NULL AND \"MicrochipNumber\" <> ''");
    }
}
