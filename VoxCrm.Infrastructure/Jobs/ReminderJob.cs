using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace VoxCrm.Infrastructure.Jobs
{
    public class ReminderJob
    {
        private readonly VoxCrmDbContext _context;
        // Veritabanına bağlanmak için kurucu metot (Dependency Injection)
        public ReminderJob(VoxCrmDbContext context)
        {
            _context = context;
        }
        // Bu metot Hangfire tarafından her gece saat 00:00'da tetiklenecek
        public async Task ProcessDailyRemindersAsync()
        {
            var today = DateTime.UtcNow.Date;
            // V1 WhatsApp kapsamı sadece aşı hatırlatmalarıdır.
            var upcomingVaccines = await _context.VaccinationRecords
                .IgnoreQueryFilters()
                .Include(v => v.Patient)
                .ThenInclude(p => p.Owners)
                .ThenInclude(o => o.PetOwner)
                .Include(v => v.VaccineType)
                .Where(v => !v.IsReminderSent)
                .ToListAsync();

            var dueVaccines = upcomingVaccines
                .Where(v => v.NextDueDate.Date == today.AddDays(v.VaccineType.ReminderDaysBefore <= 0 ? 1 : v.VaccineType.ReminderDaysBefore))
                .ToList();

            var clinicIds = dueVaccines.Select(v => v.ClinicID).Distinct().ToList();
            var clinics = await _context.Clinics
                .Where(c => clinicIds.Contains(c.ID))
                .ToDictionaryAsync(c => c.ID);

            var templates = await _context.WhatsAppTemplates
                .IgnoreQueryFilters()
                .Where(t => clinicIds.Contains(t.ClinicID)
                            && t.NotificationType == WhatsAppNotificationTypes.VaccinationReminder
                            && t.IsActive)
                .ToDictionaryAsync(t => t.ClinicID);

            foreach (var record in dueVaccines)
            {
                var primaryOwner = record.Patient.Owners.FirstOrDefault(o => o.IsPrimaryOwner)?.PetOwner;

                if (primaryOwner != null
                    && primaryOwner.WhatsAppConsent
                    && !string.IsNullOrWhiteSpace(primaryOwner.Phone))
                {
                    clinics.TryGetValue(record.ClinicID, out var clinic);
                    templates.TryGetValue(record.ClinicID, out var template);

                    var notification = new WhatsAppNotification
                    {
                        ClinicID = record.ClinicID,
                        PetOwnerId = primaryOwner.ID,
                        PhoneNumber = primaryOwner.Phone,
                        NotificationType = WhatsAppNotificationTypes.VaccinationReminder,
                        MessageContent = RenderVaccinationTemplate(template?.Body, primaryOwner, record, clinic),
                        Status = WhatsAppNotificationStatuses.Pending,
                        NextAttemptAt = DateTime.UtcNow
                    };
                    _context.WhatsAppNotifications.Add(notification);

                    record.IsReminderSent = true;
                }
            }

            await _context.SaveChangesAsync();
        }

        private static string RenderVaccinationTemplate(
            string? templateBody,
            PetOwner owner,
            VaccinationRecord record,
            Clinic? clinic)
        {
            var body = string.IsNullOrWhiteSpace(templateBody)
                ? "Sayin {ownerFirstName}, {patientName} icin {vaccineName} hatirlatmasi: sonraki tarih {dueDate}. {clinicName}"
                : templateBody;

            return body
                .Replace("{ownerFirstName}", owner.FirstName)
                .Replace("{patientName}", record.Patient.Name)
                .Replace("{vaccineName}", record.VaccineType.Name)
                .Replace("{dueDate}", record.NextDueDate.ToString("dd.MM.yyyy"))
                .Replace("{clinicName}", clinic?.Name ?? string.Empty);
        }
    }
}
