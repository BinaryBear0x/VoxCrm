using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class Patient :BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }
        public string? Name { get; set; }
        public string? Species { get; set; }
        public string? Breed { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? MicrochipNumber { get; set; }

        public string? pasaportNumarasi { get; set; }
        public char? cinsiyet { get; set; }
        public string? Notes { get; set; }

        public ICollection<PatientOwner> Owners { get; set; } = new List<PatientOwner>();
    
    }
}
