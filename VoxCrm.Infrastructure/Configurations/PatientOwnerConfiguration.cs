using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Infrastructure.Configurations;

public sealed class PatientOwnerConfiguration : IEntityTypeConfiguration<PatientOwner>
{
    public void Configure(EntityTypeBuilder<PatientOwner> builder)
    {
        builder.HasIndex(link => new { link.ClinicID, link.PatientId, link.PetOwnerId }).IsUnique();
        builder.HasIndex(link => new { link.ClinicID, link.PatientId })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE AND \"IsPrimaryOwner\" = TRUE");
    }
}
