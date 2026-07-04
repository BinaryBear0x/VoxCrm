using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;
using VoxCrm.Domain.Entities;
namespace VoxCrm.Domain.Entities
{
    public class PetOwner : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public bool WhatsAppConsent { get; set; } = false;

        public string? Notes { get; set; }

        public string? Address { get; set; }
        public ICollection<PatientOwner> OwnedPatients { get; set; } = new List<PatientOwner>();
    }
}
