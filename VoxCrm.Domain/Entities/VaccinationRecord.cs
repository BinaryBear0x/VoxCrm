using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class VaccinationRecord : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }
        // Hangi hayvana yapıldı? (Patient ile bağlantı)
        public Guid PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        // Hangi aşı yapıldı? (VaccineType ile bağlantı)
        public Guid VaccineTypeId { get; set; }
        public VaccineType VaccineType { get; set; } = null!;
        public DateTime AdministeredDate { get; set; } // Aşının vurulduğu tarih
        public DateTime NextDueDate { get; set; } // Bir sonraki aşının zamanı (Bot buraya bakacak!)

        public bool IsReminderSent { get; set; } = false; // Bot mesajı attıysa burası true olacak ki tekrar tekrar mesaj gitmesin}
    }
}
