using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class Patient :BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }
        public string? Name { get; set; } // Karabaş
        public string? Species { get; set; } // Köpek, Kedi
        public string? Breed { get; set; } // Golden, Tekir
        public DateTime? DateOfBirth { get; set; } // Doğum Tarihi
        public string? MicrochipNumber { get; set; } // Çip Numarası

        public string? pasaportNumarasi { get; set; } //passprd no 
        public char? cinsiyet { get; set; } // E erkek D dişi
        public string? Notes { get; set; } // işte herhangi bir not vs varsa bilinen hastalığı alerjisi osu busu vs. 

        // Bu hastanın sahiplerinin köprü listesi
        public ICollection<PatientOwner> Owners { get; set; } = new List<PatientOwner>();
    
    }
}

