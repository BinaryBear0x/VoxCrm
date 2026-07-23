using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class VaccineType : BaseEntity, ITenantEntity   
    {
        public Guid ClinicID { get; set; }
        public string Name { get; set; } = string.Empty;
        public int ValidityDays { get; set; }
        public int ReminderDaysBefore { get; set; }

    }
}
