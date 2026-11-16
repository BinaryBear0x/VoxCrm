using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.WhatsApp;

namespace VoxCrm.IntegrationTests;

// The recipient snapshot is compared between processes and between a freshly loaded record and one that
// was just edited in memory. The same due date must give the same snapshot whatever DateTimeKind the
// value happens to carry: right after an edit it is an Unspecified local calendar day, read back from the
// database it is Utc (or Local with legacy Npgsql timestamps).
public sealed class SnapshotDateKindTests
{
    // Local midnight of the due day, which is how the application stores a date entered in a form.
    private static readonly DateTime LocalMidnight = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Local);

    private static (PetOwner Owner, Patient Patient, VaccinationRecord Record) Data()
    {
        var clinicId = Guid.NewGuid();
        var owner = new PetOwner { ClinicID = clinicId, FirstName = "Mehmet", LastName = "Demir", Phone = "05441234567", WhatsAppConsent = true };
        var patient = new Patient { ClinicID = clinicId, Name = "Boncuk" };
        var record = new VaccinationRecord { ClinicID = clinicId, PatientId = patient.ID, VaccineTypeId = Guid.NewGuid(), NextDueDate = LocalMidnight };
        return (owner, patient, record);
    }

    private static string Snapshot((PetOwner Owner, Patient Patient, VaccinationRecord Record) data, DateTime dueDate)
    {
        data.Record.NextDueDate = dueDate;
        return WhatsAppRecipientGuard.Snapshot(data.Owner, data.Patient, data.Record);
    }

    [Fact]
    public void The_same_due_day_gives_the_same_snapshot_for_every_datetime_kind()
    {
        var data = Data();

        var edited = Snapshot(data, DateTime.SpecifyKind(LocalMidnight, DateTimeKind.Unspecified));
        var readLocal = Snapshot(data, LocalMidnight);
        var readUtc = Snapshot(data, LocalMidnight.ToUniversalTime());

        Assert.Equal(edited, readLocal);
        Assert.Equal(edited, readUtc);
    }

    [Fact]
    public void A_snapshot_written_with_any_older_date_representation_still_matches()
    {
        var data = Data();
        var legacyVariants = new[]
        {
            LocalMidnight,
            LocalMidnight.ToUniversalTime(),
            DateTime.SpecifyKind(LocalMidnight, DateTimeKind.Unspecified),
            DateTime.SpecifyKind(LocalMidnight.ToUniversalTime(), DateTimeKind.Unspecified),
            DateTime.SpecifyKind(LocalMidnight, DateTimeKind.Utc), // UTC midnight of the same day (API/test written)
        }.Select(value => WhatsAppRecipientGuard.LegacySnapshot(data.Owner, data.Patient, data.Record, value)).ToArray();

        foreach (var kind in new[] { DateTimeKind.Unspecified, DateTimeKind.Local, DateTimeKind.Utc })
        {
            data.Record.NextDueDate = kind switch
            {
                DateTimeKind.Utc => LocalMidnight.ToUniversalTime(),
                DateTimeKind.Local => LocalMidnight,
                _ => DateTime.SpecifyKind(LocalMidnight, DateTimeKind.Unspecified),
            };
            Assert.All(legacyVariants, stored => Assert.True(
                WhatsAppRecipientGuard.SnapshotMatches(stored, data.Owner, data.Patient, data.Record), $"{kind}: {stored}"));
            Assert.True(WhatsAppRecipientGuard.SnapshotMatches(
                WhatsAppRecipientGuard.Snapshot(data.Owner, data.Patient, data.Record), data.Owner, data.Patient, data.Record));
        }
    }

    [Fact]
    public void A_different_due_day_or_recipient_never_matches()
    {
        var data = Data();
        var stored = WhatsAppRecipientGuard.Snapshot(data.Owner, data.Patient, data.Record);

        data.Record.NextDueDate = LocalMidnight.AddDays(1);
        Assert.False(WhatsAppRecipientGuard.SnapshotMatches(stored, data.Owner, data.Patient, data.Record));

        data.Record.NextDueDate = LocalMidnight;
        data.Owner.LastName = "Demirci";
        Assert.False(WhatsAppRecipientGuard.SnapshotMatches(stored, data.Owner, data.Patient, data.Record));
        Assert.False(WhatsAppRecipientGuard.SnapshotMatches(null, data.Owner, data.Patient, data.Record));
        Assert.False(WhatsAppRecipientGuard.SnapshotMatches("", data.Owner, data.Patient, data.Record));
    }
}
