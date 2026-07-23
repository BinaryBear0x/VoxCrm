using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class VaccinationRecord : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }
        public Guid PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        public Guid VaccineTypeId { get; set; }
        public VaccineType VaccineType { get; set; } = null!;
        public DateTime AdministeredDate { get; set; }
        public DateTime NextDueDate { get; set; }

        public bool IsReminderSent { get; set; } = false;
    }
}
