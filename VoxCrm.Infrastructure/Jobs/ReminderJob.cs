using System;
using System.Collections.Generic;
using System.Text;
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
            // 1. YARIN Kİ AŞILARI BUL
            var upcomingVaccines = await _context.VaccinationRecords
                .Include(v => v.Patient)
                .ThenInclude(p => p.Owners)
                .ThenInclude(o => o.PetOwner)
                .Where(v => v.NextDueDate.Date == today.AddDays(1) && !v.IsReminderSent)
                .ToListAsync();
            foreach (var record in upcomingVaccines)
            {
                // Hayvanın asıl sahibini buluyoruz
                var primaryOwner = record.Patient.Owners.FirstOrDefault(o => o.IsPrimaryOwner)?.PetOwner;

                // Eğer adamın WhatsApp izni varsa mesaja hazırla
                if (primaryOwner != null && primaryOwner.WhatsAppConsent)
                {
                    var notification = new WhatsAppNotification
                    {
                        ClinicID = record.ClinicID,
                        PetOwnerId = primaryOwner.ID,
                        PhoneNumber = primaryOwner.Phone,
                        NotificationType = "Aşı",
                        MessageContent = $"Sayın {primaryOwner.FirstName}, can dostumuz {record.Patient.Name}'ın yarın aşı randevusu bulunmaktadır. Kliniğimize bekleriz.",
                        Status = "Pending" // "Bekliyor" durumunda havuza atıyoruz, bot gelip alacak.
                    };
                    _context.WhatsAppNotifications.Add(notification);

                    // Tekrar mesaj gitmesin diye "Hatırlatıldı" olarak işaretle
                    record.IsReminderSent = true;
                }
            }
            // 2. VADESİ GEÇMİŞ BORÇLARI BUL
            var overdueDebts = await _context.Borçlar
                .Include(d => d.PetOwner)
                .Where(d => !d.IsCollected && d.DueDate.Date == today.AddDays(-1)) // Vadesi 1 gün geçmiş ödenmeyenler
                .ToListAsync();
            foreach (var debt in overdueDebts)
            {
                if (debt.PetOwner.WhatsAppConsent)
                {
                    var notification = new WhatsAppNotification
                    {
                        ClinicID = debt.ClinicID,
                        PetOwnerId = debt.PetOwnerId,
                        PhoneNumber = debt.PetOwner.Phone,
                        NotificationType = "Borç",
                        MessageContent = $"Sayın {debt.PetOwner.FirstName}, kliniğimize {debt.Amount} TL gecikmiş borcunuz bulunmaktadır. Lütfen en kısa sürede ödeme yapınız.",
                        Status = "Pending"
                    };
                    _context.WhatsAppNotifications.Add(notification);
                }
            }
            // 3. YARIN Kİ RANDEVULARI BUL
            var upcomingAppointments = await _context.Appointments
                .Include(a => a.Patient)
                .ThenInclude(p => p.Owners)
                .ThenInclude(o => o.PetOwner)
                .Where(a => a.ScheduledAt.Date == today.AddDays(1) && a.Status == "Planlandı")
                .ToListAsync();

            foreach (var appointment in upcomingAppointments)
            {
                var primaryOwner = appointment.Patient.Owners.FirstOrDefault(o => o.IsPrimaryOwner)?.PetOwner;
                if (primaryOwner != null && primaryOwner.WhatsAppConsent)
                {
                    var notification = new WhatsAppNotification
                    {
                        ClinicID = appointment.ClinicID,
                        PetOwnerId = primaryOwner.ID,
                        PhoneNumber = primaryOwner.Phone,
                        NotificationType = "Randevu",
                        MessageContent = $"Sayın {primaryOwner.FirstName}, can dostumuz {appointment.Patient.Name}'ın yarın saat {appointment.ScheduledAt.ToLocalTime():HH:mm}'de kliniğimizde randevusu bulunmaktadır. Lütfen gecikmeyiniz.",
                        Status = "Pending"
                    };
                    _context.WhatsAppNotifications.Add(notification);
                }
            }

            // Tüm hazırlanan mesajları veritabanına tek seferde kaydet
            await _context.SaveChangesAsync();
        }
    }
}
