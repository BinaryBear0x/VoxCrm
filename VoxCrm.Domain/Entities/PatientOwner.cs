using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class PatientOwner : BaseEntity, ITenantEntity

    {
        public Guid ClinicID { get; set; }
        public Guid PetOwnerId { get; set; } // Sahiplenen kişinin ID'si
        public PetOwner PetOwner { get; set; } = null!; // Yazılımın kişiyi tanıması için bağlantı
        public Guid PatientId { get; set; } // Sahiplenilen hayvanın ID'si
        public Patient Patient { get; set; } = null!; // sistemin hayvanı tanıması için bağlantı
        public bool IsPrimaryOwner { get; set; } = true; // Asıl sahibi mi? (Fatura kime kesilecek?)
    }
}


