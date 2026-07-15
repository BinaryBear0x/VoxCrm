using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Audit;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Examinations;
using VoxCrm.Infrastructure.Patients;
using VoxCrm.Infrastructure.PetOwners;
using VoxCrm.Infrastructure.ServiceItems;
using VoxCrm.Infrastructure.Vaccinations;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ArchiveVaccinationIntegrationTests
{
    private readonly PostgresDatabaseFixture _database;

    public ArchiveVaccinationIntegrationTests(PostgresDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Invalid_owner_patient_create_leaves_no_patient_row()
    {
        var data = await SeedTwoClinicsAsync();
        var patient = new Patient { Name = "Must Not Persist" };

        await using (var tenantDb = CreateTenantContext(data.ClinicA.ID))
        {
            var service = new PatientService(tenantDb, new FixedTenantService(data.ClinicA.ID), new DbAuditLogger(tenantDb));
            var result = await service.CreateAsync(patient, data.OwnerB.ID);
            Assert.False(result.Succeeded);
        }

        await using var verification = _database.CreateDbContext();
        Assert.False(await verification.Patients.IgnoreQueryFilters().AnyAsync(item => item.ID == patient.ID));
        Assert.False(await verification.PatientOwners.IgnoreQueryFilters().AnyAsync(item => item.PatientId == patient.ID));
    }

    [Fact]
    public async Task Unknown_animal_and_person_can_be_recorded_with_no_contact_or_identity_data()
    {
        var data = await SeedTwoClinicsAsync();
        await using var tenantDb = CreateTenantContext(data.ClinicA.ID);
        var tenant = new FixedTenantService(data.ClinicA.ID);
        var audit = new DbAuditLogger(tenantDb);

        var patient = await new PatientService(tenantDb, tenant, audit)
            .CreateAsync(new Patient { Notes = "Sokakta bulundu" }, ownerId: null);
        var person = await new PetOwnerService(tenantDb, tenant, audit)
            .CreateAsync(new PetOwner { Notes = "Kimlik ve iletişim bilgisi yok" });

        Assert.True(patient.Succeeded);
        Assert.True(person.Succeeded);
        Assert.StartsWith("Kimliği belirsiz hayvan ", patient.Patient!.Name);
        Assert.StartsWith("Kimliği belirsiz kişi ", person.Owner!.FirstName);
        Assert.Null(person.Owner.Phone);
        Assert.Equal(data.ClinicA.ID, patient.Patient.ClinicID);
        Assert.Equal(data.ClinicA.ID, person.Owner.ClinicID);
    }

    [Fact]
    public async Task Malformed_or_excessive_intake_data_is_rejected_without_partial_rows()
    {
        var data = await SeedTwoClinicsAsync();
        await using var tenantDb = CreateTenantContext(data.ClinicA.ID);
        var tenant = new FixedTenantService(data.ClinicA.ID);
        var audit = new DbAuditLogger(tenantDb);
        var patients = new PatientService(tenantDb, tenant, audit);
        var owners = new PetOwnerService(tenantDb, tenant, audit);

        var futureBirthInput = new Patient { DateOfBirth = DateTime.UtcNow.AddDays(1) };
        var invalidGenderInput = new Patient { cinsiyet = 'X' };
        var futureBirth = await patients.CreateAsync(futureBirthInput, null);
        var invalidGender = await patients.CreateAsync(invalidGenderInput, null);
        var hugeNote = await patients.CreateAsync(new Patient { Notes = new string('x', 2001) }, null);
        var invalidPhone = await owners.CreateAsync(new PetOwner { Phone = "123" });
        var invalidEmail = await owners.CreateAsync(new PetOwner { Email = "not-an-email" });

        Assert.False(futureBirth.Succeeded);
        Assert.False(invalidGender.Succeeded);
        Assert.False(hugeNote.Succeeded);
        Assert.False(invalidPhone.Succeeded);
        Assert.False(invalidEmail.Succeeded);
        Assert.False(await tenantDb.Patients.AnyAsync(patient =>
            patient.ID == futureBirthInput.ID || patient.ID == invalidGenderInput.ID));
    }

    [Fact]
    public async Task Tenant_services_reject_foreign_patient_owner_and_vaccination_mutations()
    {
        var data = await SeedTwoClinicsAsync(includeHistory: true);
        await using var tenantDb = CreateTenantContext(data.ClinicA.ID);
        var tenant = new FixedTenantService(data.ClinicA.ID);
        var audit = new DbAuditLogger(tenantDb);
        var patientService = new PatientService(tenantDb, tenant, audit);
        var ownerService = new PetOwnerService(tenantDb, tenant, audit);
        var vaccinationService = new VaccinationService(tenantDb, tenant, audit);

        var patientResult = await patientService.ArchiveAsync(data.PatientB!.ID, Guid.NewGuid());
        var ownerResult = await ownerService.ArchiveAsync(data.OwnerB.ID, Guid.NewGuid());
        var vaccinationResult = await vaccinationService.ArchiveAsync(data.RecordB!.ID, Guid.NewGuid());

        Assert.True(patientResult.NotFound);
        Assert.True(ownerResult.NotFound);
        Assert.True(vaccinationResult.NotFound);

        await using var verification = _database.CreateDbContext();
        Assert.True((await verification.Patients.IgnoreQueryFilters().SingleAsync(item => item.ID == data.PatientB.ID)).IsActive);
        Assert.True((await verification.PetOwners.IgnoreQueryFilters().SingleAsync(item => item.ID == data.OwnerB.ID)).IsActive);
        Assert.True((await verification.VaccinationRecords.IgnoreQueryFilters().SingleAsync(item => item.ID == data.RecordB.ID)).IsActive);
    }

    [Fact]
    public async Task Archiving_patient_owner_vaccine_and_record_preserves_related_history()
    {
        var data = await SeedTwoClinicsAsync(includeHistory: true);
        var actorUserId = Guid.NewGuid();
        await using (var tenantDb = CreateTenantContext(data.ClinicA.ID))
        {
            var tenant = new FixedTenantService(data.ClinicA.ID);
            var audit = new DbAuditLogger(tenantDb);
            var patientService = new PatientService(tenantDb, tenant, audit);
            var ownerService = new PetOwnerService(tenantDb, tenant, audit);
            var vaccinationService = new VaccinationService(tenantDb, tenant, audit);
            var vaccineTypeService = new VaccineTypeService(tenantDb, tenant, audit);

            Assert.True((await patientService.ArchiveAsync(data.PatientA!.ID, actorUserId)).Succeeded);
            Assert.True((await ownerService.ArchiveAsync(data.OwnerA.ID, actorUserId)).Succeeded);
            Assert.True((await vaccinationService.ArchiveAsync(data.RecordA!.ID, actorUserId)).Succeeded);
            Assert.True((await vaccineTypeService.ArchiveAsync(data.VaccineA!.ID, actorUserId)).Succeeded);
        }

        await using var verification = _database.CreateDbContext();
        var patient = await verification.Patients.IgnoreQueryFilters().SingleAsync(item => item.ID == data.PatientA!.ID);
        var owner = await verification.PetOwners.IgnoreQueryFilters().SingleAsync(item => item.ID == data.OwnerA.ID);
        var vaccine = await verification.VaccineTypes.IgnoreQueryFilters().SingleAsync(item => item.ID == data.VaccineA!.ID);
        var record = await verification.VaccinationRecords.IgnoreQueryFilters().SingleAsync(item => item.ID == data.RecordA!.ID);

        Assert.False(patient.IsActive);
        Assert.False(owner.IsActive);
        Assert.False(vaccine.IsActive);
        Assert.False(record.IsActive);
        Assert.NotNull(patient.ArchivedAt);
        Assert.NotNull(owner.ArchivedAt);
        Assert.NotNull(vaccine.ArchivedAt);
        Assert.NotNull(record.ArchivedAt);
        Assert.Equal(actorUserId, patient.ArchivedByUserId);
        Assert.Equal(actorUserId, owner.ArchivedByUserId);
        Assert.Equal(actorUserId, vaccine.ArchivedByUserId);
        Assert.Equal(actorUserId, record.ArchivedByUserId);

        Assert.True(await verification.PatientOwners.IgnoreQueryFilters().AnyAsync(
            link => link.PatientId == patient.ID && link.PetOwnerId == owner.ID));
        Assert.True(await verification.Appointments.IgnoreQueryFilters().AnyAsync(item => item.PatientId == patient.ID));
        Assert.True(await verification.Muayeneler.IgnoreQueryFilters().AnyAsync(item => item.PatientId == patient.ID));
        Assert.True(await verification.Borçlar.IgnoreQueryFilters().AnyAsync(item => item.PetOwnerId == owner.ID));
        Assert.Equal(patient.ID, record.PatientId);
        Assert.Equal(vaccine.ID, record.VaccineTypeId);
    }

    [Fact]
    public async Task Restoring_owner_rejects_an_active_duplicate_phone()
    {
        var data = await SeedTwoClinicsAsync();
        var tenant = new FixedTenantService(data.ClinicA.ID);
        await using var tenantDb = CreateTenantContext(data.ClinicA.ID);
        var service = new PetOwnerService(tenantDb, tenant, new DbAuditLogger(tenantDb));

        Assert.True((await service.ArchiveAsync(data.OwnerA.ID, Guid.NewGuid())).Succeeded);
        var replacement = await service.CreateAsync(new PetOwner
        {
            FirstName = "Replacement",
            Phone = data.OwnerA.Phone,
        });
        Assert.True(replacement.Succeeded);

        var restore = await service.RestoreAsync(data.OwnerA.ID, Guid.NewGuid());

        Assert.False(restore.Succeeded);
        Assert.Contains("aktif bir müşteri", restore.Error);
    }

    [Fact]
    public async Task Adding_a_second_owner_preserves_the_existing_primary_owner()
    {
        var data = await SeedTwoClinicsAsync(includeHistory: true);
        var tenant = new FixedTenantService(data.ClinicA.ID);
        await using var tenantDb = CreateTenantContext(data.ClinicA.ID);
        var service = new PetOwnerService(tenantDb, tenant, new DbAuditLogger(tenantDb));
        var secondOwner = await service.CreateAsync(new PetOwner
        {
            FirstName = "Secondary Owner",
            Phone = $"+90{Random.Shared.NextInt64(1000000000, 9999999999)}",
        });
        Assert.True(secondOwner.Succeeded);

        var result = await service.AddPatientAsync(secondOwner.Owner!.ID, data.PatientA!.ID);

        Assert.True(result.Succeeded);
        var links = await tenantDb.PatientOwners
            .Where(link => link.PatientId == data.PatientA.ID && link.IsActive)
            .ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Single(links, link => link.IsPrimaryOwner);
        Assert.Contains(links, link => link.PetOwnerId == secondOwner.Owner.ID && !link.IsPrimaryOwner);
    }

    [Fact]
    public async Task Service_item_and_examination_can_be_archived_and_restored()
    {
        var data = await SeedTwoClinicsAsync(includeHistory: true);
        await using var tenantDb = CreateTenantContext(data.ClinicA.ID);
        var tenant = new FixedTenantService(data.ClinicA.ID);
        var serviceItems = new ServiceItemService(tenantDb, tenant);
        var examinations = new ExaminationService(tenantDb, tenant);
        var actor = Guid.NewGuid();

        var serviceItem = await serviceItems.CreateAsync(new ServiceItem
        {
            Name = $"Restore Service {Guid.NewGuid():N}",
            Price = 100,
        });
        Assert.True(serviceItem.Succeeded);
        var examination = await tenantDb.Muayeneler.SingleAsync(
            item => item.PatientId == data.PatientA!.ID && item.IsActive);

        Assert.True((await serviceItems.ArchiveAsync(serviceItem.Item!.ID, actor)).Succeeded);
        Assert.True((await examinations.ArchiveAsync(examination.ID, actor)).Succeeded);
        Assert.DoesNotContain(await serviceItems.ListAsync(), item => item.ID == serviceItem.Item.ID);
        Assert.DoesNotContain(await examinations.ListAsync(), item => item.ID == examination.ID);
        Assert.Contains(await serviceItems.ListAsync(includeArchived: true), item => item.ID == serviceItem.Item.ID && !item.IsActive);
        Assert.Contains(await examinations.ListAsync(includeArchived: true), item => item.ID == examination.ID && !item.IsActive);

        Assert.True((await serviceItems.RestoreAsync(serviceItem.Item.ID, actor)).Succeeded);
        Assert.True((await examinations.RestoreAsync(examination.ID, actor)).Succeeded);
        Assert.Contains(await serviceItems.ListAsync(), item => item.ID == serviceItem.Item.ID && item.IsActive);
        Assert.Contains(await examinations.ListAsync(), item => item.ID == examination.ID && item.IsActive);
    }

    [Fact]
    public async Task Vaccination_create_requires_active_same_tenant_references()
    {
        var data = await SeedTwoClinicsAsync(includeHistory: true);
        await using var tenantDb = CreateTenantContext(data.ClinicA.ID);
        var tenant = new FixedTenantService(data.ClinicA.ID);
        var service = new VaccinationService(tenantDb, tenant, new DbAuditLogger(tenantDb));
        var administered = new DateTime(2026, 7, 9, 10, 0, 0, DateTimeKind.Utc);

        var foreignPatient = await service.CreateAsync(NewRecord(data.PatientB!.ID, data.VaccineA!.ID, administered));
        var foreignVaccine = await service.CreateAsync(NewRecord(data.PatientA!.ID, data.VaccineB!.ID, administered));

        data.PatientA.IsActive = false;
        await using (var systemDb = _database.CreateDbContext())
        {
            var patient = await systemDb.Patients.SingleAsync(item => item.ID == data.PatientA.ID);
            patient.IsActive = false;
            await systemDb.SaveChangesAsync();
        }
        var archivedPatient = await service.CreateAsync(NewRecord(data.PatientA.ID, data.VaccineA.ID, administered));

        await using (var systemDb = _database.CreateDbContext())
        {
            var patient = await systemDb.Patients.SingleAsync(item => item.ID == data.PatientA.ID);
            patient.IsActive = true;
            var vaccine = await systemDb.VaccineTypes.SingleAsync(item => item.ID == data.VaccineA.ID);
            vaccine.IsActive = false;
            await systemDb.SaveChangesAsync();
        }
        tenantDb.ChangeTracker.Clear();
        var archivedVaccine = await service.CreateAsync(NewRecord(data.PatientA.ID, data.VaccineA.ID, administered));

        await using (var systemDb = _database.CreateDbContext())
        {
            var vaccine = await systemDb.VaccineTypes.SingleAsync(item => item.ID == data.VaccineA.ID);
            vaccine.IsActive = true;
            await systemDb.SaveChangesAsync();
        }
        tenantDb.ChangeTracker.Clear();
        var valid = await service.CreateAsync(NewRecord(data.PatientA.ID, data.VaccineA.ID, administered));
        var choices = await service.GetChoicesAsync();

        Assert.False(foreignPatient.Succeeded);
        Assert.False(foreignVaccine.Succeeded);
        Assert.False(archivedPatient.Succeeded);
        Assert.False(archivedVaccine.Succeeded);
        Assert.True(valid.Succeeded);
        Assert.NotNull(valid.Record);
        Assert.Equal(administered.AddDays(data.VaccineA.ValidityDays), valid.Record!.NextDueDate);
        Assert.All(choices.Patients, patient => Assert.Equal(data.ClinicA.ID, patient.ClinicID));
        Assert.All(choices.VaccineTypes, vaccine => Assert.Equal(data.ClinicA.ID, vaccine.ClinicID));
        Assert.DoesNotContain(choices.Patients, patient => patient.ID == data.PatientB.ID);
        Assert.DoesNotContain(choices.VaccineTypes, vaccine => vaccine.ID == data.VaccineB.ID);
    }

    [Fact]
    public async Task Vaccination_rejects_a_future_or_missing_administered_date()
    {
        var data = await SeedTwoClinicsAsync(includeHistory: true);
        await using var tenantDb = CreateTenantContext(data.ClinicA.ID);
        var tenant = new FixedTenantService(data.ClinicA.ID);
        var service = new VaccinationService(tenantDb, tenant, new DbAuditLogger(tenantDb));

        var future = await service.CreateAsync(NewRecord(data.PatientA!.ID, data.VaccineA!.ID, DateTime.UtcNow.AddDays(1)));
        var missing = await service.CreateAsync(NewRecord(data.PatientA.ID, data.VaccineA.ID, default));

        Assert.False(future.Succeeded);
        Assert.False(missing.Succeeded);
    }

    [Fact]
    public async Task Vaccine_validity_and_reminder_bounds_are_enforced()
    {
        var data = await SeedTwoClinicsAsync();
        await using var tenantDb = CreateTenantContext(data.ClinicA.ID);
        var tenant = new FixedTenantService(data.ClinicA.ID);
        var service = new VaccineTypeService(tenantDb, tenant, new DbAuditLogger(tenantDb));

        Assert.False((await service.CreateAsync(NewVaccine("Zero", 0, 0))).Succeeded);
        Assert.False((await service.CreateAsync(NewVaccine("Too Long", 3651, 0))).Succeeded);
        Assert.False((await service.CreateAsync(NewVaccine("Negative Reminder", 30, -1))).Succeeded);
        Assert.False((await service.CreateAsync(NewVaccine("Late Reminder", 30, 31))).Succeeded);
        Assert.True((await service.CreateAsync(NewVaccine("Lower Bound", 1, 0))).Succeeded);
        Assert.True((await service.CreateAsync(NewVaccine("Upper Bound", 3650, 3650))).Succeeded);

        await using var verification = _database.CreateDbContext();
        var saved = await verification.VaccineTypes.IgnoreQueryFilters()
            .Where(item => item.ClinicID == data.ClinicA.ID)
            .Select(item => item.Name)
            .ToListAsync();
        Assert.Contains("Lower Bound", saved);
        Assert.Contains("Upper Bound", saved);
        Assert.DoesNotContain("Zero", saved);
        Assert.DoesNotContain("Too Long", saved);
        Assert.DoesNotContain("Negative Reminder", saved);
        Assert.DoesNotContain("Late Reminder", saved);
    }

    private async Task<SeedData> SeedTwoClinicsAsync(bool includeHistory = false)
    {
        await using var db = _database.CreateDbContext();
        await TestData.ClearWhatsAppDataAsync(db);

        var dealerA = CreateDealer("Archive Dealer A");
        var dealerB = CreateDealer("Archive Dealer B");
        var clinicA = CreateClinic(dealerA, "Archive Clinic A");
        var clinicB = CreateClinic(dealerB, "Archive Clinic B");
        var ownerA = CreateOwner(clinicA.ID, "Owner A");
        var ownerB = CreateOwner(clinicB.ID, "Owner B");
        db.AddRange(dealerA, dealerB, clinicA, clinicB, ownerA, ownerB);

        Patient? patientA = null;
        Patient? patientB = null;
        VaccineType? vaccineA = null;
        VaccineType? vaccineB = null;
        VaccinationRecord? recordA = null;
        VaccinationRecord? recordB = null;
        if (includeHistory)
        {
            patientA = new Patient { ClinicID = clinicA.ID, Name = "Patient A" };
            patientB = new Patient { ClinicID = clinicB.ID, Name = "Patient B" };
            vaccineA = NewVaccine("Vaccine A", 365, 7);
            vaccineA.ClinicID = clinicA.ID;
            vaccineB = NewVaccine("Vaccine B", 180, 5);
            vaccineB.ClinicID = clinicB.ID;
            recordA = NewRecord(patientA.ID, vaccineA.ID, DateTime.UtcNow.AddDays(-10));
            recordA.ClinicID = clinicA.ID;
            recordA.NextDueDate = recordA.AdministeredDate.AddDays(vaccineA.ValidityDays);
            recordB = NewRecord(patientB.ID, vaccineB.ID, DateTime.UtcNow.AddDays(-5));
            recordB.ClinicID = clinicB.ID;
            recordB.NextDueDate = recordB.AdministeredDate.AddDays(vaccineB.ValidityDays);
            db.AddRange(
                patientA,
                patientB,
                vaccineA,
                vaccineB,
                recordA,
                recordB,
                new PatientOwner
                {
                    ClinicID = clinicA.ID,
                    PatientId = patientA.ID,
                    PetOwnerId = ownerA.ID,
                    IsPrimaryOwner = true,
                },
                new Appointment
                {
                    ClinicID = clinicA.ID,
                    PatientId = patientA.ID,
                    ScheduledAt = DateTime.UtcNow,
                    AppointmentType = "Control",
                },
                new Muayene
                {
                    ClinicID = clinicA.ID,
                    PatientId = patientA.ID,
                    Assessment = "Historical examination",
                },
                new Debt
                {
                    ClinicID = clinicA.ID,
                    PetOwnerId = ownerA.ID,
                    Description = "Historical debt",
                    Amount = 100,
                    DueDate = DateTime.UtcNow.AddDays(30),
                });
        }

        await db.SaveChangesAsync();
        return new SeedData(clinicA, clinicB, ownerA, ownerB, patientA, patientB, vaccineA, vaccineB, recordA, recordB);
    }

    private VoxCrmDbContext CreateTenantContext(Guid clinicId)
    {
        var options = new DbContextOptionsBuilder<VoxCrmDbContext>()
            .UseNpgsql(
                _database.ConnectionString,
                builder => builder.MigrationsAssembly("VoxCrm.Infrastructure"))
            .Options;
        return new VoxCrmDbContext(options, new FixedTenantService(clinicId));
    }

    private static VaccinationRecord NewRecord(Guid patientId, Guid vaccineTypeId, DateTime administeredDate) =>
        new()
        {
            PatientId = patientId,
            VaccineTypeId = vaccineTypeId,
            AdministeredDate = administeredDate,
        };

    private static VaccineType NewVaccine(string name, int validityDays, int reminderDays) =>
        new()
        {
            Name = name,
            ValidityDays = validityDays,
            ReminderDaysBefore = reminderDays,
        };

    private static Dealer CreateDealer(string name) =>
        new()
        {
            CompanyName = name,
            ContactEmail = $"{name.Replace(' ', '-').ToLowerInvariant()}@example.test",
            ContactPhone = "+905550000000",
        };

    private static Clinic CreateClinic(Dealer dealer, string name) =>
        new()
        {
            Name = name,
            Slug = name.Replace(' ', '-').ToLowerInvariant(),
            DealerId = dealer.ID,
            Dealer = dealer,
        };

    private static PetOwner CreateOwner(Guid clinicId, string firstName)
    {
        var phone = $"+90{Random.Shared.NextInt64(1000000000, 9999999999)}";
        return new PetOwner
        {
            ClinicID = clinicId,
            FirstName = firstName,
            Phone = phone,
            NormalizedPhone = new string(phone.Where(char.IsDigit).ToArray()),
        };
    }

    private sealed record SeedData(
        Clinic ClinicA,
        Clinic ClinicB,
        PetOwner OwnerA,
        PetOwner OwnerB,
        Patient? PatientA,
        Patient? PatientB,
        VaccineType? VaccineA,
        VaccineType? VaccineB,
        VaccinationRecord? RecordA,
        VaccinationRecord? RecordB);

    private sealed class FixedTenantService : ITenantService
    {
        private readonly Guid _clinicId;

        public FixedTenantService(Guid clinicId)
        {
            _clinicId = clinicId;
        }

        public Guid GetClinicId() => _clinicId;
        public bool IsSystemContext => false;
    }
}
