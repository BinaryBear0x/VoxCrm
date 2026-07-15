using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Appointments;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Appointments;
using VoxCrm.Infrastructure.Data;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class AppointmentIntegrationTests
{
    private readonly PostgresDatabaseFixture _database;

    public AppointmentIntegrationTests(PostgresDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Clinic_local_time_is_stored_as_utc_and_returned_as_clinic_local()
    {
        var (clinic, patient) = await SeedClinicAndPatientAsync("timezone", "Europe/Istanbul");
        await using var db = CreateTenantDbContext(clinic.ID);
        var service = new AppointmentService(db, new FixedTenantService(clinic.ID));
        var local = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Unspecified);

        var result = await service.CreateAsync(
            new AppointmentCommand(patient.ID, local, "Muayene", 30, "Kontrol"),
            confirmConflict: false);

        Assert.True(result.Succeeded);
        var stored = await db.Appointments
            .IgnoreQueryFilters()
            .SingleAsync(appointment => appointment.ID == result.AppointmentId);
        Assert.Equal(new DateTime(2026, 1, 15, 7, 30, 0, DateTimeKind.Utc), stored.ScheduledAt);

        var displayed = await service.GetAsync(stored.ID);
        Assert.NotNull(displayed);
        Assert.Equal(local, displayed.ScheduledAtLocal);
        Assert.Equal(DateTimeKind.Unspecified, displayed.ScheduledAtLocal.Kind);
    }

    [Fact]
    public async Task Conflict_returns_typed_warning_then_confirm_rechecks_and_saves()
    {
        var (clinic, patient) = await SeedClinicAndPatientAsync("conflict", "Europe/Istanbul");
        await using var db = CreateTenantDbContext(clinic.ID);
        var service = new AppointmentService(db, new FixedTenantService(clinic.ID));
        var first = new AppointmentCommand(
            patient.ID,
            new DateTime(2026, 2, 10, 10, 0, 0),
            "Muayene",
            60,
            null);
        var overlapping = first with
        {
            ScheduledAtLocal = new DateTime(2026, 2, 10, 10, 30, 0),
            AppointmentType = "Kontrol"
        };

        Assert.True((await service.CreateAsync(first, confirmConflict: false)).Succeeded);
        var warning = await service.CreateAsync(overlapping, confirmConflict: false);

        Assert.Equal(AppointmentCommandOutcome.ConflictWarning, warning.Outcome);
        Assert.Equal(1, await CountActiveAppointmentsAsync(db, clinic.ID));

        var confirmed = await service.CreateAsync(overlapping, confirmConflict: true);
        Assert.True(confirmed.Succeeded);
        Assert.Equal(2, await CountActiveAppointmentsAsync(db, clinic.ID));
    }

    [Fact]
    public async Task Concurrent_duplicate_booking_attempts_cannot_both_save_without_a_conflict_warning()
    {
        var (clinic, patient) = await SeedClinicAndPatientAsync("concurrent", "Europe/Istanbul");
        var command = new AppointmentCommand(
            patient.ID,
            new DateTime(2026, 8, 10, 14, 0, 0),
            "Muayene",
            30,
            null);

        await using var dbA = CreateTenantDbContext(clinic.ID);
        await using var dbB = CreateTenantDbContext(clinic.ID);
        var results = await Task.WhenAll(
            new AppointmentService(dbA, new FixedTenantService(clinic.ID)).CreateAsync(command, confirmConflict: false),
            new AppointmentService(dbB, new FixedTenantService(clinic.ID)).CreateAsync(command, confirmConflict: false));

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => result.Outcome == AppointmentCommandOutcome.ConflictWarning);
        await using var verification = CreateTenantDbContext(clinic.ID);
        Assert.Equal(1, await CountActiveAppointmentsAsync(verification, clinic.ID));
    }

    [Fact]
    public async Task Tenant_cannot_read_or_mutate_another_clinics_appointments_and_delete_archives()
    {
        var (clinicA, patientA) = await SeedClinicAndPatientAsync("tenant-a", "Europe/Istanbul");
        var (clinicB, patientB) = await SeedClinicAndPatientAsync("tenant-b", "Europe/London");
        Guid appointmentBId;

        await using (var dbB = CreateTenantDbContext(clinicB.ID))
        {
            var serviceB = new AppointmentService(dbB, new FixedTenantService(clinicB.ID));
            var created = await serviceB.CreateAsync(
                new AppointmentCommand(patientB.ID, new DateTime(2026, 3, 20, 12, 0, 0), "Aşı", 30, null),
                confirmConflict: false);
            Assert.True(created.Succeeded);
            appointmentBId = created.AppointmentId!.Value;
        }

        await using var dbA = CreateTenantDbContext(clinicA.ID);
        var serviceA = new AppointmentService(dbA, new FixedTenantService(clinicA.ID));
        Assert.Null(await serviceA.GetAsync(appointmentBId));
        Assert.Equal(
            AppointmentCommandOutcome.ValidationFailed,
            (await serviceA.CreateAsync(
                new AppointmentCommand(patientB.ID, new DateTime(2026, 3, 20, 12, 0, 0), "Aşı", 30, null),
                confirmConflict: false)).Outcome);

        var own = await serviceA.CreateAsync(
            new AppointmentCommand(patientA.ID, new DateTime(2026, 3, 20, 12, 0, 0), "Aşı", 30, null),
            confirmConflict: false);
        var actorId = Guid.NewGuid();
        Assert.True((await serviceA.ArchiveAsync(own.AppointmentId!.Value, actorId)).Succeeded);

        var archived = await dbA.Appointments
            .IgnoreQueryFilters()
            .SingleAsync(appointment => appointment.ID == own.AppointmentId);
        Assert.False(archived.IsActive);
        Assert.NotNull(archived.ArchivedAt);
        Assert.Equal(actorId, archived.ArchivedByUserId);
        Assert.Empty(await serviceA.ListAsync(null));
        Assert.Contains(
            await serviceA.ListAsync(null, includeArchived: true),
            appointment => appointment.Id == own.AppointmentId && !appointment.IsActive);

        Assert.True((await serviceA.RestoreAsync(own.AppointmentId.Value, actorId)).Succeeded);
        var restored = await dbA.Appointments
            .IgnoreQueryFilters()
            .SingleAsync(appointment => appointment.ID == own.AppointmentId);
        Assert.True(restored.IsActive);
        Assert.Null(restored.ArchivedAt);
        Assert.Null(restored.ArchivedByUserId);
    }

    private async Task<(Clinic Clinic, Patient Patient)> SeedClinicAndPatientAsync(
        string suffix,
        string timeZoneId)
    {
        await using var db = _database.CreateDbContext();
        var dealer = new Dealer
        {
            CompanyName = $"Appointment {suffix}",
            ContactEmail = $"{suffix}-{Guid.NewGuid():N}@example.test",
            ContactPhone = "+905550000000"
        };
        var clinic = new Clinic
        {
            Name = $"Appointment {suffix}",
            Slug = $"{suffix}-{Guid.NewGuid():N}",
            Dealer = dealer,
            DealerId = dealer.ID,
            TimeZoneId = timeZoneId
        };
        var patient = new Patient
        {
            ClinicID = clinic.ID,
            Name = $"Patient {suffix}",
            Species = "Kedi"
        };
        db.AddRange(dealer, clinic, patient);
        await db.SaveChangesAsync();
        return (clinic, patient);
    }

    private VoxCrmDbContext CreateTenantDbContext(Guid clinicId)
    {
        var options = new DbContextOptionsBuilder<VoxCrmDbContext>()
            .UseNpgsql(_database.ConnectionString, builder => builder.MigrationsAssembly("VoxCrm.Infrastructure"))
            .Options;
        return new VoxCrmDbContext(options, new FixedTenantService(clinicId));
    }

    private static Task<int> CountActiveAppointmentsAsync(VoxCrmDbContext db, Guid clinicId) =>
        db.Appointments.IgnoreQueryFilters()
            .CountAsync(appointment => appointment.ClinicID == clinicId && appointment.IsActive);

    private sealed class FixedTenantService(Guid clinicId) : ITenantService
    {
        public Guid GetClinicId() => clinicId;
        public bool IsSystemContext => false;
    }
}
