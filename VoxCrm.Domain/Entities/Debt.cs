using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class Debt : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }

        // Bu borç kimin?
        public Guid PetOwnerId { get; set; }
        public PetOwner PetOwner { get; set; } = null!;
        public string Description { get; set; } = string.Empty; // "Muayene ve İç Dış Parazit Ücreti"
        public decimal Amount { get; set; } // Tutar (Örn: 1500.00)

        public DateTime DueDate { get; set; } // Son ödeme tarihi (Borçlu müşterilere bot mesaj atacak)

        // İşin kalbi: Tahsil edildi mi?
        public bool IsCollected { get; set; } = false;

        public DateTime? CollectedAt { get; set; } // Ne zaman tahsil edildi? (Eğer tahsil edildiyse)
        public string? PaymentMethod { get; set; } // Nakit, Kredi Kartı vb. (Eğer tahsil edildiyse)

        public DateTime? CancelledAt { get; set; }
        public Guid? CancelledByUserId { get; set; }
        public string? CancellationReason { get; set; }

        // Tahsilatlar
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
