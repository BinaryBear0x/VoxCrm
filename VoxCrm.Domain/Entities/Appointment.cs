using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;
namespace VoxCrm.Domain.Entities
{
    public class Appointment : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }

         // Randevu hangi hayvana ait?
        public Guid PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        public DateTime ScheduledAt { get; set; } // Randevu tarihi ve saati
        public int DurationMinutes { get; set; } = 30; // Kaç dakika sürecek? (Takvimde kutu çizmek için)
        
        public string AppointmentType { get; set; } = string.Empty; // Muayene, Aşı, Tıraş, Ameliyat vb.
        public string Status { get; set; } = "Planlandı"; // Planlandı, Tamamlandı, İptal, Gelmedi
        
        public string? Reason { get; set; } // Şikayet / Geliş Sebebi
        public bool IsReminderSent { get; set; } = false; // Bot randevu hatırlatması attı mı?
    }
}
