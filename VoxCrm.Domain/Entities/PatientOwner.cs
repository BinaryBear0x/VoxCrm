using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class PatientOwner : BaseEntity, ITenantEntity

    {
        public Guid ClinicID { get; set; }
        public Guid PetOwnerId { get; set; }
        public PetOwner PetOwner { get; set; } = null!;
        public Guid PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        public bool IsPrimaryOwner { get; set; } = true;
    }
}

