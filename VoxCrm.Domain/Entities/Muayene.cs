using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;   
namespace VoxCrm.Domain.Entities
{
    public class Muayene : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set;}


        public Guid PatientId { get; set; }
        public Patient Patient { get; set; } = null!;
        public Guid? AppointmentId { get; set; } // Eğer bu muayene önceden alınmış bir randevuya bağlıysa
        public Appointment? Appointment { get; set; }
        // S = Subjective (Öznel)
        public string? Subjective { get; set; } // Hasta sahibinin anlattıkları (Örn: "2 gündür kusuyor, hiçbir şey yemiyor")

        // O = Objective (Nesnel)
        public string? Objective { get; set; } // Hekimin fiziksel muayene bulguları (Örn: "Ateş 39.5, diş etleri soluk")

        // A = Assessment (Teşhis/Değerlendirme)
        public string? Assessment { get; set; } // Hekimin Teşhisi (Örn: "Viral Enfeksiyon şüphesi")

        // P = Plan (Tedavi Planı)
        public string? Plan { get; set; } // Uygulanacak tedavi, serum, reçete
        public decimal? WeightAtVisit { get; set; } // O günkü kilosu (Kilo grafiği çizmek için çok önemli)
        public decimal? Temperature { get; set; } // O günkü ateşi
    }
}
