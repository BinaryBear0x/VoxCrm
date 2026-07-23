using System;
using System.Collections.Generic;
using System.Text;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class Debt : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }

        public Guid PetOwnerId { get; set; }
        public PetOwner PetOwner { get; set; } = null!;
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }

        public DateTime DueDate { get; set; }

        public bool IsCollected { get; set; } = false;

        public DateTime? CollectedAt { get; set; }
        public string? PaymentMethod { get; set; }

        public DateTime? CancelledAt { get; set; }
        public Guid? CancelledByUserId { get; set; }
        public string? CancellationReason { get; set; }

        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
