using System;
using System.ComponentModel.DataAnnotations.Schema;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class Payment : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }

        public Guid DebtId { get; set; }
        public Debt Debt { get; set; } = null!;

        public PaymentEntryType EntryType { get; set; } = PaymentEntryType.Payment;
        public decimal Amount { get; set; } // Ödenen miktar
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
        public string PaymentMethod { get; set; } = string.Empty; // Nakit, Kredi Kartı vb.

        public Guid? ReversesPaymentId { get; set; }
        [ForeignKey(nameof(ReversesPaymentId))]
        [InverseProperty(nameof(Reversal))]
        public Payment? ReversesPayment { get; set; }
        [InverseProperty(nameof(ReversesPayment))]
        public Payment? Reversal { get; set; }
        public string? Reason { get; set; }
        public Guid? ActorUserId { get; set; }

        // Ödemeyi yapan kişi genelde borcun sahibidir ama not düşmek istenebilir.
        public string? Notes { get; set; }
    }
}
