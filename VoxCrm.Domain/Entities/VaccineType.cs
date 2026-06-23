using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class VaccineType : BaseEntity, ITenantEntity   
    {
        public Guid ClinicID { get; set; }
        public string Name { get; set; } = string.Empty; // Örn: "Kuduz Aşısı", "Karma Aşı"
        public int ValidityDays { get; set; } // Bu aşı kaç gün geçerli? (Örn: 365)
        public int ReminderDaysBefore { get; set; } // WhatsApp Botu kaç gün önceden hatırlatma atsın? (Örn: 7)

    }
}
