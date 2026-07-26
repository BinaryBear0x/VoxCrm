using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Jobs;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class ReminderJobIntegrationTests
{
    private readonly PostgresDatabaseFixture _database;

    public ReminderJobIntegrationTests(PostgresDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Daily_job_queues_only_due_vaccinations_for_enabled_clinics()
    {
        Guid enabledRecordId;
        Guid disabledRecordId;

        await using (var db = _database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            enabledRecordId = await AddDueVaccinationAsync(db, "Enabled Reminder", whatsAppEnabled: true);
            disabledRecordId = await AddDueVaccinationAsync(db, "Disabled Reminder", whatsAppEnabled: false);
        }

        await using (var jobDb = _database.CreateDbContext())
        {
            var job = new ReminderJob(jobDb);
            await job.ProcessDailyRemindersAsync();
            await job.ProcessDailyRemindersAsync();
        }

        await using var verify = _database.CreateDbContext();
        var notifications = await verify.WhatsAppNotifications
            .IgnoreQueryFilters()
            .ToListAsync();

        var notification = Assert.Single(notifications);
        Assert.Equal(WhatsAppNotificationTypes.VaccinationReminder, notification.NotificationType);
        Assert.Equal(WhatsAppNotificationStatuses.Pending, notification.Status);
        Assert.Contains("Kuduz", notification.MessageContent);

        var enabledRecord = await verify.VaccinationRecords
            .IgnoreQueryFilters()
            .SingleAsync(record => record.ID == enabledRecordId);
        var disabledRecord = await verify.VaccinationRecords
            .IgnoreQueryFilters()
            .SingleAsync(record => record.ID == disabledRecordId);

        Assert.True(enabledRecord.IsReminderSent);
        Assert.False(disabledRecord.IsReminderSent);
    }

    private static async Task<Guid> AddDueVaccinationAsync(
        VoxCrmDbContext db,
        string clinicName,
        bool whatsAppEnabled)
    {
        var (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(
            db,
            clinicName,
            whatsAppEnabled);
        var patient = new Patient
        {
            ClinicID = clinic.ID,
            Name = "Boncuk",
            Species = "Kedi",
        };
        var ownership = new PatientOwner
        {
            ClinicID = clinic.ID,
            Patient = patient,
            PatientId = patient.ID,
            PetOwner = owner,
            PetOwnerId = owner.ID,
            IsPrimaryOwner = true,
        };
        var vaccineType = new VaccineType
        {
            ClinicID = clinic.ID,
            Name = "Kuduz",
            ValidityDays = 365,
            ReminderDaysBefore = 3,
        };
        var vaccination = new VaccinationRecord
        {
            ClinicID = clinic.ID,
            Patient = patient,
            PatientId = patient.ID,
            VaccineType = vaccineType,
            VaccineTypeId = vaccineType.ID,
            AdministeredDate = DateTime.UtcNow.Date.AddDays(-362),
            NextDueDate = DateTime.UtcNow.Date.AddDays(3),
        };

        db.AddRange(patient, ownership, vaccineType, vaccination);
        await db.SaveChangesAsync();
        return vaccination.ID;
    }
}
