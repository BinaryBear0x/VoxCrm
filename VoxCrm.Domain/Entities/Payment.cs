using System;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class Payment : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }

        public Guid DebtId { get; set; }
        public Debt Debt { get; set; } = null!;

        public decimal Amount { get; set; } // Ödenen miktar
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
        public string PaymentMethod { get; set; } = string.Empty; // Nakit, Kredi Kartı vb.
        
        // Ödemeyi yapan kişi genelde borcun sahibidir ama not düşmek istenebilir.
        public string? Notes { get; set; }
    }
}
