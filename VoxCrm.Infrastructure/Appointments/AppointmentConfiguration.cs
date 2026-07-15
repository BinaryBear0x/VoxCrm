using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Infrastructure.Appointments;

public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.HasIndex(appointment => new { appointment.ClinicID, appointment.ScheduledAt });
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Appointments_DurationMinutes",
            "\"DurationMinutes\" BETWEEN 10 AND 240"));
    }
}
