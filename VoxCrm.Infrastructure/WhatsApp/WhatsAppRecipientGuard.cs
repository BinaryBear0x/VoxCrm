using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.WhatsApp;

// No network calls. Both the queue and gateway use this immediately before dispatch.
public sealed class WhatsAppRecipientGuard(VoxCrmDbContext db)
{
    public static string Digits(string? phone)
    {
        var d = new string((phone ?? "").Where(char.IsAsciiDigit).ToArray());
        // International dialing prefix: "0090555..." and "+90555..." are the same number.
        if (d.StartsWith("00", StringComparison.Ordinal)) d = d[2..];
        if (d.Length == 11 && d.StartsWith('0')) d = "90" + d[1..];
        if (d.Length == 10) d = "90" + d;
        return d;
    }
    public static string Snapshot(PetOwner owner, Patient? patient, VaccinationRecord? record = null) =>
        Hash(owner, patient, record, record == null ? null : CalendarDay(record.NextDueDate));

    /// <summary>
    /// True when <paramref name="stored"/> describes this recipient. The due date inside the snapshot used to be
    /// serialized with whatever DateTimeKind the value had: Unspecified straight after an edit (a local calendar
    /// day), Utc or Local once read back from the database. The same due date therefore produced different
    /// hashes in different code paths and a correct message was stopped as "recipient changed".
    /// New snapshots use the calendar day only; snapshots written by earlier versions stay valid because they
    /// are matched against every representation they could have used.
    /// </summary>
    public static bool SnapshotMatches(string? stored, PetOwner owner, Patient? patient, VaccinationRecord? record = null)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        if (stored == Snapshot(owner, patient, record)) return true;
        if (record == null) return false;
        return LegacyRepresentations(record.NextDueDate).Any(value => stored == Hash(owner, patient, record, value));
    }

    /// <summary>Snapshot as an earlier version would have written it for the given date representation (test seam).</summary>
    public static string LegacySnapshot(PetOwner owner, Patient? patient, VaccinationRecord record, DateTime dueDateAsStored) =>
        Hash(owner, patient, record, dueDateAsStored);

    // The application treats due dates as calendar days of the clinic's local time (DateTime.Date is used
    // everywhere). A Utc value read from the database is converted back to that local day; Local and
    // Unspecified values already are local wall-clock values.
    private static DateTime CalendarDay(DateTime value) =>
        DateTime.SpecifyKind((value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value).Date, DateTimeKind.Unspecified);

    private static IEnumerable<DateTime> LegacyRepresentations(DateTime value)
    {
        var day = CalendarDay(value);
        var asLocal = DateTime.SpecifyKind(day, DateTimeKind.Local);
        var asUtcMidnight = DateTime.SpecifyKind(day, DateTimeKind.Utc);
        var candidates = new List<DateTime>
        {
            value,
            asLocal, asLocal.ToUniversalTime(),
            asUtcMidnight, asUtcMidnight.ToLocalTime(),
            DateTime.SpecifyKind(asLocal.ToUniversalTime(), DateTimeKind.Unspecified),
        };
        // The same value as another Kind (what a different process would have held).
        candidates.Add(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        candidates.Add(DateTime.SpecifyKind(value, DateTimeKind.Unspecified));
        candidates.Add(value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value.ToUniversalTime());
        // No Distinct(): DateTime equality ignores Kind, but Kind changes how the value is serialized.
        return candidates;
    }

    // Property names and order are part of the stored hash: do not rename or reorder them.
    private static string Hash(PetOwner owner, Patient? patient, VaccinationRecord? record, DateTime? nextDueDate) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { owner.ClinicID, owner.ID, owner.FirstName, owner.LastName, Phone = Digits(owner.Phone), owner.WhatsAppConsent,
            PatientId = patient?.ID, PatientName = patient?.Name, VaccinationId = record?.ID, record?.VaccineTypeId, NextDueDate = nextDueDate }))));

    public async Task<string?> CurrentSnapshotAsync(WhatsAppNotification n, CancellationToken ct = default)
    {
        var owner = await db.PetOwners.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.ID == n.PetOwnerId && x.ClinicID == n.ClinicID, ct);
        if (owner == null) return null;
        VaccinationRecord? record = null;
        if (n.VaccinationRecordId.HasValue)
        {
            record = await db.VaccinationRecords.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.ID == n.VaccinationRecordId && x.ClinicID == n.ClinicID, ct);
            if (record == null) return null;
        }
        var patientId = record?.PatientId ?? n.PatientId;
        Patient? patient = null;
        if (patientId.HasValue)
        {
            patient = await db.Patients.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.ID == patientId && x.ClinicID == n.ClinicID, ct);
            if (patient == null) return null;
        }
        return Snapshot(owner, patient, record);
    }

    public async Task<string?> CheckAsync(WhatsAppNotification n, CancellationToken ct = default)
    {
        var clinic = await db.Clinics.AsNoTracking().SingleOrDefaultAsync(x => x.ID == n.ClinicID, ct);
        if (clinic == null || !clinic.IsActive || !clinic.IsWhatsAppEnabled) return "Klinik veya WhatsApp gönderimi kapalı. Ayarlardan kontrol edin.";
        if (n.IsAutomatic && !clinic.AutomaticVaccinationRemindersEnabled) return "Otomatik aşı hatırlatmaları kapatıldı.";
        if (!n.IsAutomatic && !clinic.ManualWhatsAppEnabled) return "Manuel gönderim kapatıldı.";
        var owner = await db.PetOwners.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.ID == n.PetOwnerId && x.ClinicID == n.ClinicID, ct);
        if (owner == null || !owner.IsActive) return "Müşteri silinmiş veya arşivlenmiş. Gönderim durduruldu.";
        if (!owner.WhatsAppConsent) return "Müşterinin WhatsApp izni yok. Gönderim durduruldu.";
        var phone = Digits(owner.Phone);
        if (phone.Length is < 10 or > 15 || phone != Digits(n.PhoneNumber)) return "Alıcı telefonu değişmiş veya geçersiz. Alıcıyı yeniden doğrulayın.";
        Guid? patientId = n.PatientId;
        VaccinationRecord? record = null;
        if (n.VaccinationRecordId.HasValue)
        {
            record = await db.VaccinationRecords.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.ID == n.VaccinationRecordId && x.ClinicID == n.ClinicID, ct);
            if (record == null || !record.IsActive || record.NextDueDate.Date < VoxCrm.Domain.Common.ClinicTime.Today(clinic.WhatsAppTimeZoneId).ToDateTime(TimeOnly.MinValue)) return "Aşı kaydı silinmiş veya tarihi geçmiş.";
            if (await db.VaccinationRecords.IgnoreQueryFilters().AnyAsync(x => x.ClinicID == n.ClinicID && x.IsActive && x.PatientId == record.PatientId && x.VaccineTypeId == record.VaccineTypeId && x.AdministeredDate.Date > record.AdministeredDate.Date, ct)) return "Bu uygulama yenilenmiş. Eski hatırlatma gönderimi durduruldu.";
            patientId = record.PatientId;
        }
        Patient? patient = null;
        if (patientId.HasValue)
        {
            patient = await db.Patients.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.ID == patientId && x.ClinicID == n.ClinicID, ct);
            if (patient == null || !patient.IsActive) return "İlgili hayvan silinmiş veya arşivlenmiş.";
            var links = await db.PatientOwners.IgnoreQueryFilters().AsNoTracking().Where(x => x.ClinicID == n.ClinicID && x.PatientId == patientId && x.IsActive).ToListAsync(ct);
            if (!links.Any(x => x.PetOwnerId == owner.ID)) return "Hayvanın müşteriyle sahip bağlantısı değişmiş. Gönderim durduruldu.";
            if (n.VaccinationRecordId.HasValue && links.SingleOrDefault(x => x.IsPrimaryOwner)?.PetOwnerId != owner.ID)
                return "Hayvanın birincil sahibi değişmiş. Gönderim durduruldu.";
        }
        if (!SnapshotMatches(n.RecipientSnapshot, owner, patient, record))
            return "Alıcı bilgileri değişmiş veya eski mesaj doğrulanmamış. Önizlemeden alıcıyı doğrulayarak yeniden hazırlayın.";
        return null;
    }
}
