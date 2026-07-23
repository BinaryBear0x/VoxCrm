using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;
namespace VoxCrm.Domain.Entities
{
    public class Appointment : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }

        public Guid PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        public DateTime ScheduledAt { get; set; }
        public int DurationMinutes { get; set; } = 30;

        public string AppointmentType { get; set; } = string.Empty;
        public string Status { get; set; } = "Planlandı";

        public string? Reason { get; set; }
        public bool IsReminderSent { get; set; } = false;
    }
}
