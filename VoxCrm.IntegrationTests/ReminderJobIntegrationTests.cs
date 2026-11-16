using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Jobs;
using VoxCrm.Infrastructure.Vaccinations;
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
            var job = CreateJob(jobDb);
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
        Assert.Equal(enabledRecordId, notification.VaccinationRecordId);
        Assert.Contains("Kuduz", notification.MessageContent);
        Assert.Contains("Ayse Test", notification.MessageContent);
        Assert.Contains("Veteriner Hekim Ebru Hanım", notification.MessageContent);
        Assert.DoesNotContain("{ownerFirstName}", notification.MessageContent, StringComparison.Ordinal);
        Assert.DoesNotContain("SERİ-ABC-123", notification.MessageContent, StringComparison.Ordinal);

        var enabledRecord = await verify.VaccinationRecords
            .IgnoreQueryFilters()
            .SingleAsync(record => record.ID == enabledRecordId);
        var disabledRecord = await verify.VaccinationRecords
            .IgnoreQueryFilters()
            .SingleAsync(record => record.ID == disabledRecordId);

        Assert.True(enabledRecord.IsReminderSent);
        Assert.False(disabledRecord.IsReminderSent);
    }

    [Fact]
    public async Task Daily_job_recovers_an_upcoming_vaccination_added_after_its_exact_reminder_day()
    {
        Guid vaccinationId;
        await using (var db = _database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            vaccinationId = await AddDueVaccinationAsync(
                db,
                "Late Added Reminder",
                whatsAppEnabled: true,
                nextDueInDays: 1,
                reminderDaysBefore: 5);
        }

        await using (var jobDb = _database.CreateDbContext())
        {
            var job = CreateJob(jobDb);
            await job.ProcessDailyRemindersAsync();
            await job.ProcessDailyRemindersAsync();
        }

        await using var verify = _database.CreateDbContext();
        var notification = Assert.Single(await verify.WhatsAppNotifications.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(vaccinationId, notification.VaccinationRecordId);
        Assert.Equal(WhatsAppNotificationStatuses.Pending, notification.Status);
        Assert.True((await verify.VaccinationRecords.IgnoreQueryFilters().SingleAsync(v => v.ID == vaccinationId)).IsReminderSent);
    }

    [Fact]
    public async Task Daily_job_does_not_send_an_overdue_vaccination()
    {
        Guid vaccinationId;
        await using (var db = _database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            vaccinationId = await AddDueVaccinationAsync(
                db,
                "Overdue Reminder",
                whatsAppEnabled: true,
                nextDueInDays: -1,
                reminderDaysBefore: 5);
        }

        await using (var jobDb = _database.CreateDbContext())
            await CreateJob(jobDb).ProcessDailyRemindersAsync();

        await using var verify = _database.CreateDbContext();
        Assert.Empty(await verify.WhatsAppNotifications.IgnoreQueryFilters().ToListAsync());
        Assert.False((await verify.VaccinationRecords.IgnoreQueryFilters().SingleAsync(v => v.ID == vaccinationId)).IsReminderSent);
    }

    [Fact]
    public async Task Concurrent_reconciliation_creates_only_one_notification()
    {
        Guid vaccinationId;
        await using (var db = _database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            vaccinationId = await AddDueVaccinationAsync(
                db,
                "Concurrent Reminder",
                whatsAppEnabled: true,
                nextDueInDays: 1,
                reminderDaysBefore: 5);
        }

        await using var firstDb = _database.CreateDbContext();
        await using var secondDb = _database.CreateDbContext();
        var first = new VaccinationReminderScheduler(firstDb, new ClinicSendWindowCalculator());
        var second = new VaccinationReminderScheduler(secondDb, new ClinicSendWindowCalculator());

        await Task.WhenAll(
            first.ReconcileAsync(vaccinationId),
            second.ReconcileAsync(vaccinationId));

        await using var verify = _database.CreateDbContext();
        Assert.Equal(1, await verify.WhatsAppNotifications.IgnoreQueryFilters()
            .CountAsync(notification => notification.VaccinationRecordId == vaccinationId));
    }

    [Fact]
    public async Task Immediate_send_bypasses_queue_and_failed_send_can_be_requeued()
    {
        Guid vaccinationId;
        await using (var db = _database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            vaccinationId = await AddDueVaccinationAsync(
                db,
                "Immediate Reminder",
                whatsAppEnabled: true,
                nextDueInDays: 1,
                reminderDaysBefore: 5);
        }

        await using (var db = _database.CreateDbContext())
        {
            var scheduler = new VaccinationReminderScheduler(db, new ClinicSendWindowCalculator());
            await scheduler.ReconcileAsync(vaccinationId);

            var dispatch = await scheduler.StartImmediateAsync(vaccinationId);
            Assert.True(dispatch.Succeeded);
            Assert.Equal((await db.WhatsAppNotifications.SingleAsync()).ID, dispatch.RequestId);
            var direct = await db.WhatsAppNotifications.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(WhatsAppNotificationStatuses.DirectSending, direct.Status);
            Assert.Null(direct.NextAttemptAt);

            await scheduler.FailImmediateAsync(vaccinationId, "provider error");
            Assert.Equal(WhatsAppNotificationStatuses.Failed, direct.Status);

            Assert.True((await scheduler.RetryFailedAsync(vaccinationId)).Succeeded);
            Assert.Equal(WhatsAppNotificationStatuses.Pending, direct.Status);

            Assert.True((await scheduler.StartImmediateAsync(vaccinationId)).Succeeded);
            await scheduler.CompleteImmediateAsync(vaccinationId, "message-123");
            Assert.Equal(WhatsAppNotificationStatuses.Sent, direct.Status);
            Assert.Equal("message-123", direct.GatewayMessageId);
            Assert.NotNull(direct.SentAt);
        }

        await using var verify = _database.CreateDbContext();
        Assert.Equal(1, await verify.WhatsAppNotifications.IgnoreQueryFilters()
            .CountAsync(notification => notification.VaccinationRecordId == vaccinationId));
    }

    private static async Task<Guid> AddDueVaccinationAsync(
        VoxCrmDbContext db,
        string clinicName,
        bool whatsAppEnabled,
        int nextDueInDays = 3,
        int reminderDaysBefore = 3)
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
            ReminderDaysBefore = reminderDaysBefore,
        };
        var vaccination = new VaccinationRecord
        {
            ClinicID = clinic.ID,
            Patient = patient,
            PatientId = patient.ID,
            VaccineType = vaccineType,
            VaccineTypeId = vaccineType.ID,
            AdministeredDate = DateTime.UtcNow.Date.AddDays(nextDueInDays - 365),
            NextDueDate = DateTime.UtcNow.Date.AddDays(nextDueInDays),
            VaccineCode = "SERİ-ABC-123",
        };

        db.AddRange(patient, ownership, vaccineType, vaccination);
        await db.SaveChangesAsync();
        return vaccination.ID;
    }

    private static ReminderJob CreateJob(VoxCrmDbContext db) =>
        new(new VaccinationReminderScheduler(db, new ClinicSendWindowCalculator()));
}
