using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Vaccinations;
using VoxCrm.Infrastructure.WhatsApp;
using VoxCrm.IntegrationTests.Infrastructure;

namespace VoxCrm.IntegrationTests;

public sealed class SendWindowDaylightSavingTests
{
    private static ClinicSendWindowInfo Window(string zone, int startHour, int startMinute, int endHour) => new(
        Guid.NewGuid(), "Clinic", true, new TimeOnly(startHour, startMinute), new TimeOnly(endHour, 0), zone, null);

    [Fact]
    public void A_window_start_inside_the_spring_forward_gap_opens_at_the_first_valid_moment()
    {
        // Berlin switches from 02:00 to 03:00 on 2027-03-28, so 02:30 does not exist that day.
        var decision = new ClinicSendWindowCalculator().GetDecision(
            Window("Europe/Berlin", 2, 30, 6), new DateTime(2027, 3, 27, 20, 0, 0, DateTimeKind.Utc));

        Assert.False(decision.IsOpen);
        Assert.Equal(new DateTime(2027, 3, 28, 1, 0, 0, DateTimeKind.Utc), decision.NextAllowedUtc);
    }

    [Fact]
    public void A_window_start_that_happens_twice_opens_at_its_first_occurrence()
    {
        // Berlin repeats 02:00-03:00 on 2027-10-31; the first 02:30 is still summer time (UTC+2).
        var decision = new ClinicSendWindowCalculator().GetDecision(
            Window("Europe/Berlin", 2, 30, 6), new DateTime(2027, 10, 30, 20, 0, 0, DateTimeKind.Utc));

        Assert.False(decision.IsOpen);
        Assert.Equal(new DateTime(2027, 10, 31, 0, 30, 0, DateTimeKind.Utc), decision.NextAllowedUtc);
    }

    [Fact]
    public void Ordinary_days_and_unknown_zones_keep_working()
    {
        var calculator = new ClinicSendWindowCalculator();

        var istanbul = calculator.GetDecision(Window("Europe/Istanbul", 9, 0, 19), new DateTime(2027, 6, 1, 20, 0, 0, DateTimeKind.Utc));
        Assert.False(istanbul.IsOpen);
        Assert.Equal(new DateTime(2027, 6, 2, 6, 0, 0, DateTimeKind.Utc), istanbul.NextAllowedUtc); // 09:00 at UTC+3

        var unknown = calculator.GetDecision(Window("Mars/Olympus", 9, 0, 19), new DateTime(2027, 6, 1, 20, 0, 0, DateTimeKind.Utc));
        Assert.Equal(istanbul.NextAllowedUtc, unknown.NextAllowedUtc);
    }
}

public sealed class VaccinationMessageTemplateTests
{
    private static VaccinationMessageValues Values(string? last = "Test", string? first = "Ayşe", string? patient = "Boncuk") =>
        new(first, last, patient, "Kuduz", new DateTime(2027, 5, 4), "Klinik X");

    [Fact]
    public void A_missing_last_name_does_not_leave_a_stray_space_before_the_comma()
    {
        var message = VaccinationMessageTemplate.Render(null, Values(last: null));

        Assert.StartsWith("Merhaba Ayşe, Boncuk için Kuduz zamanı: 04.05.2027.", message);
        Assert.DoesNotContain("  ", message);
    }

    [Fact]
    public void Values_that_look_like_placeholders_are_not_expanded_again()
    {
        var message = VaccinationMessageTemplate.Render("{ownerFirstName} / {patientName}", Values(first: "{patientName}", patient: "Boncuk"));

        Assert.Equal("{patientName} / Boncuk", message);
    }

    [Fact]
    public void Placeholders_are_case_insensitive_and_unknown_ones_stay_visible()
    {
        var message = VaccinationMessageTemplate.Render("{OwnerFirstName}: {dueDATE} {typo}", Values());

        Assert.Equal("Ayşe: 04.05.2027 {typo}", message);
    }

    [Fact]
    public void A_missing_first_name_falls_back_to_a_neutral_greeting()
    {
        Assert.Contains("Değerli hayvan sahibi", VaccinationMessageTemplate.Render("Merhaba {ownerFirstName}", Values(first: " ")));
    }

    [Fact]
    public void The_legacy_default_text_is_upgraded_to_the_current_default()
    {
        Assert.Equal(
            VaccinationMessageTemplate.Render(null, Values()),
            VaccinationMessageTemplate.Render(VaccinationMessageTemplate.LegacyBody, Values()));
    }
}

public sealed class PhoneDigitsTests
{
    [Theory]
    [InlineData("+90 555 111 22 33", "905551112233")]
    [InlineData("05551112233", "905551112233")]
    [InlineData("5551112233", "905551112233")]
    [InlineData("905551112233", "905551112233")]
    [InlineData("0090 555 111 22 33", "905551112233")]
    [InlineData("+44 20 7946 0018", "442079460018")]
    [InlineData("", "")]
    public void Every_common_spelling_of_a_number_gives_the_same_digits(string input, string expected) =>
        Assert.Equal(expected, WhatsAppRecipientGuard.Digits(input));
}

[Collection(DatabaseCollection.Name)]
public sealed class ReminderReconciliationTests(PostgresDatabaseFixture database)
{
    private static async Task<(Clinic Clinic, Guid[] RecordIds)> AddClinicWithRecordsAsync(
        VoxCrmDbContext db, string name, int count, int nextDueInDays = 2, int reminderDaysBefore = 5)
    {
        var (clinic, owner) = await TestData.CreateClinicWithOwnerAsync(db, name);
        var vaccineType = new VaccineType { ClinicID = clinic.ID, Name = "Kuduz", ValidityDays = 365, ReminderDaysBefore = reminderDaysBefore };
        db.Add(vaccineType);
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var patient = new Patient { ClinicID = clinic.ID, Name = $"Hayvan {i}" };
            var record = new VaccinationRecord
            {
                ClinicID = clinic.ID, Patient = patient, PatientId = patient.ID, VaccineType = vaccineType, VaccineTypeId = vaccineType.ID,
                AdministeredDate = DateTime.UtcNow.Date.AddDays(nextDueInDays - 365), NextDueDate = DateTime.UtcNow.Date.AddDays(nextDueInDays),
            };
            db.AddRange(patient, record, new PatientOwner { ClinicID = clinic.ID, Patient = patient, PatientId = patient.ID, PetOwner = owner, PetOwnerId = owner.ID, IsPrimaryOwner = true });
            ids.Add(record.ID);
        }
        await db.SaveChangesAsync();
        return (clinic, ids.ToArray());
    }

    [Fact]
    public async Task A_bulk_run_larger_than_one_chunk_queues_every_due_reminder_once()
    {
        Clinic clinic;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, _) = await AddClinicWithRecordsAsync(db, "Bulk Reminder", 120);
        }

        await using (var job = database.CreateDbContext())
        {
            var scheduler = new VaccinationReminderScheduler(job, new ClinicSendWindowCalculator());
            Assert.Equal(120, await scheduler.ReconcileAsync());
            Assert.Equal(0, await scheduler.ReconcileAsync());
        }

        await using var verify = database.CreateDbContext();
        Assert.Equal(120, await verify.WhatsAppNotifications.IgnoreQueryFilters().CountAsync(n => n.ClinicID == clinic.ID));
    }

    [Fact]
    public async Task Vaccinations_whose_reminder_day_is_far_away_are_not_touched_by_the_daily_run()
    {
        Guid farRecord;
        Guid nearRecord;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            var far = await AddClinicWithRecordsAsync(db, "Far Reminder", 1, nextDueInDays: 400, reminderDaysBefore: 5);
            var near = await AddClinicWithRecordsAsync(db, "Near Reminder", 1, nextDueInDays: 2, reminderDaysBefore: 5);
            farRecord = far.RecordIds[0];
            nearRecord = near.RecordIds[0];
        }

        await using (var job = database.CreateDbContext())
            Assert.Equal(1, await new VaccinationReminderScheduler(job, new ClinicSendWindowCalculator()).ReconcileAsync());

        await using var verify = database.CreateDbContext();
        var queued = await verify.WhatsAppNotifications.IgnoreQueryFilters().Select(n => n.VaccinationRecordId).ToListAsync();
        Assert.Equal(new Guid?[] { nearRecord }, queued);
        Assert.DoesNotContain(farRecord, queued.Select(id => id!.Value));
    }

    [Fact]
    public async Task A_queued_reminder_is_cancelled_even_when_the_due_date_moved_far_into_the_future()
    {
        Guid recordId;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (_, var ids) = await AddClinicWithRecordsAsync(db, "Moved Reminder", 1, nextDueInDays: 2);
            recordId = ids[0];
        }

        await using (var job = database.CreateDbContext())
            Assert.Equal(1, await new VaccinationReminderScheduler(job, new ClinicSendWindowCalculator()).ReconcileAsync());

        await using (var db = database.CreateDbContext())
            await db.VaccinationRecords.IgnoreQueryFilters().Where(r => r.ID == recordId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.NextDueDate, DateTime.UtcNow.Date.AddDays(400)));

        await using (var job = database.CreateDbContext())
            await new VaccinationReminderScheduler(job, new ClinicSendWindowCalculator()).ReconcileAsync();

        await using var verify = database.CreateDbContext();
        Assert.Equal(WhatsAppNotificationStatuses.Cancelled, (await verify.WhatsAppNotifications.IgnoreQueryFilters().SingleAsync()).Status);
    }

    [Fact]
    public async Task One_clinic_that_cannot_be_written_does_not_stop_the_other_clinics()
    {
        Clinic bad;
        Clinic good;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (bad, _) = await AddClinicWithRecordsAsync(db, "Failing Clinic", 2);
            (good, _) = await AddClinicWithRecordsAsync(db, "Healthy Clinic", 2);
            // Simulates a data problem that only affects one clinic's rows.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE OR REPLACE FUNCTION fail_for_one_clinic() RETURNS trigger AS $$
                BEGIN
                  IF NEW."ClinicID" = '{0}'::uuid THEN RAISE EXCEPTION 'simulated failure'; END IF;
                  RETURN NEW;
                END; $$ LANGUAGE plpgsql;
                """.Replace("{0}", bad.ID.ToString()));
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_for_one_clinic_trigger BEFORE INSERT ON "WhatsAppNotifications"
                FOR EACH ROW EXECUTE FUNCTION fail_for_one_clinic();
                """);
        }

        try
        {
            await using var job = database.CreateDbContext();
            var scheduler = new VaccinationReminderScheduler(job, new ClinicSendWindowCalculator());

            // The run reports the problem at the end, after every other record was handled.
            await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.ReconcileAsync());

            await using var verify = database.CreateDbContext();
            var perClinic = await verify.WhatsAppNotifications.IgnoreQueryFilters()
                .GroupBy(n => n.ClinicID).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            Assert.Equal(2, perClinic.Single(x => x.Key == good.ID).Count);
            Assert.DoesNotContain(perClinic, x => x.Key == bad.ID);
        }
        finally
        {
            await using var cleanup = database.CreateDbContext();
            await cleanup.Database.ExecuteSqlRawAsync("""DROP TRIGGER IF EXISTS fail_for_one_clinic_trigger ON "WhatsAppNotifications";""");
            await cleanup.Database.ExecuteSqlRawAsync("DROP FUNCTION IF EXISTS fail_for_one_clinic();");
        }
    }

    [Fact]
    public async Task The_forecast_shows_what_enabling_automatic_reminders_would_do_without_writing_anything()
    {
        Clinic clinic;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, _) = await AddClinicWithRecordsAsync(db, "Forecast Clinic", 3, nextDueInDays: 2, reminderDaysBefore: 5);
            // The switch is off: the forecast must still answer "what would go?".
            await db.Clinics.Where(c => c.ID == clinic.ID).ExecuteUpdateAsync(s => s.SetProperty(c => c.AutomaticVaccinationRemindersEnabled, false));
            var owners = await db.PatientOwners.IgnoreQueryFilters().Where(o => o.ClinicID == clinic.ID).ToListAsync();
            // Second animal: its owner has no consent; third: owner has no phone (own owners, same clinic).
            var secondOwner = new PetOwner { ClinicID = clinic.ID, FirstName = "Izinsiz", LastName = "Musteri", Phone = "+905550000001", WhatsAppConsent = false };
            var thirdOwner = new PetOwner { ClinicID = clinic.ID, FirstName = "Telefonsuz", LastName = "Musteri", Phone = null, WhatsAppConsent = true };
            db.AddRange(secondOwner, thirdOwner);
            await db.SaveChangesAsync();
            await db.PatientOwners.IgnoreQueryFilters().Where(o => o.ID == owners[1].ID).ExecuteUpdateAsync(s => s.SetProperty(o => o.PetOwnerId, secondOwner.ID));
            await db.PatientOwners.IgnoreQueryFilters().Where(o => o.ID == owners[2].ID).ExecuteUpdateAsync(s => s.SetProperty(o => o.PetOwnerId, thirdOwner.ID));
        }

        await using var read = database.CreateDbContext();
        var preview = await new VaccinationReminderScheduler(read, new ClinicSendWindowCalculator()).PreviewAsync(clinic.ID);

        Assert.Equal(1, preview.TotalEligible);
        Assert.Equal(1, preview.SkippedNoConsent);
        Assert.Equal(1, preview.SkippedNoPhone);
        var item = Assert.Single(preview.Items);
        Assert.True(item.GoesNow);
        Assert.Contains("Kuduz", item.Message);
        await using var verify = database.CreateDbContext();
        Assert.Empty(await verify.WhatsAppNotifications.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task The_forecast_leaves_out_records_that_already_have_a_message_and_far_away_vaccinations()
    {
        Clinic clinic;
        await using (var db = database.CreateDbContext())
        {
            await TestData.ClearWhatsAppDataAsync(db);
            (clinic, _) = await AddClinicWithRecordsAsync(db, "Forecast Filter Clinic", 1, nextDueInDays: 2, reminderDaysBefore: 5);
            await AddMoreRecordsAsync(db, clinic, nextDueInDays: 200);
        }
        await using (var job = database.CreateDbContext())
            await new VaccinationReminderScheduler(job, new ClinicSendWindowCalculator()).ReconcileAsync();

        await using var read = database.CreateDbContext();
        var preview = await new VaccinationReminderScheduler(read, new ClinicSendWindowCalculator()).PreviewAsync(clinic.ID);

        Assert.Equal(0, preview.TotalEligible);
        Assert.Equal(1, preview.AlreadyQueued);
    }

    private static async Task AddMoreRecordsAsync(VoxCrmDbContext db, Clinic clinic, int nextDueInDays)
    {
        var owner = await db.PetOwners.IgnoreQueryFilters().FirstAsync(o => o.ClinicID == clinic.ID);
        var type = await db.VaccineTypes.IgnoreQueryFilters().FirstAsync(t => t.ClinicID == clinic.ID);
        var patient = new Patient { ClinicID = clinic.ID, Name = "Uzak" };
        var record = new VaccinationRecord
        {
            ClinicID = clinic.ID, Patient = patient, PatientId = patient.ID, VaccineType = type, VaccineTypeId = type.ID,
            AdministeredDate = DateTime.UtcNow.Date.AddDays(nextDueInDays - 365), NextDueDate = DateTime.UtcNow.Date.AddDays(nextDueInDays),
        };
        db.AddRange(patient, record, new PatientOwner { ClinicID = clinic.ID, Patient = patient, PatientId = patient.ID, PetOwner = owner, PetOwnerId = owner.ID, IsPrimaryOwner = true });
        await db.SaveChangesAsync();
    }
}
