using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Infrastructure.Configurations;

public sealed class PetOwnerConfiguration : IEntityTypeConfiguration<PetOwner>
{
    public void Configure(EntityTypeBuilder<PetOwner> builder)
    {
        builder.Property(owner => owner.NormalizedPhone).HasMaxLength(64);
        builder.Property(owner => owner.EmailLookupHash).HasMaxLength(64);
        builder.HasIndex(owner => new { owner.ClinicID, owner.NormalizedPhone })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE AND \"NormalizedPhone\" IS NOT NULL");
        builder.HasIndex(owner => new { owner.ClinicID, owner.EmailLookupHash });
    }
}
