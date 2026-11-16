using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class VaccinationRecord : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }
        public Guid? SubmissionId { get; set; }
        public Guid? BatchSubmissionId { get; set; }
        public string? BatchFingerprint { get; set; }
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public DateTime? RenewedOn { get; set; }
        public Guid PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        public Guid VaccineTypeId { get; set; }
        public VaccineType VaccineType { get; set; } = null!;
        public DateTime AdministeredDate { get; set; }
        public DateTime NextDueDate { get; set; }
        public string? VaccineCode { get; set; }

        public bool IsReminderSent { get; set; } = false;
        public ICollection<WhatsAppNotification> WhatsAppNotifications { get; set; } = new List<WhatsAppNotification>();
    }
}
