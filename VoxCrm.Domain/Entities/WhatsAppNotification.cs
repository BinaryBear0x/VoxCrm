using System.ComponentModel.DataAnnotations.Schema;
using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class WhatsAppNotification : BaseEntity, ITenantEntity       
    {
        public Guid ClinicID { get; set; }

        public Guid PetOwnerId { get; set; }
        public PetOwner PetOwner { get; set; } = null!;
        public string PhoneNumber { get; set; } = string.Empty;
        public string MessageContent { get; set; } = string.Empty;
        public string NotificationType { get; set; } = WhatsAppNotificationTypes.VaccinationReminder;
        public string Status { get; set; } = WhatsAppNotificationStatuses.Pending;

        public DateTime? LockedAt { get; set; }
        public string? LockedBy { get; set; }
        public DateTime? LockExpiresAt { get; set; }
        public int RetryCount { get; set; }
        public DateTime? NextAttemptAt { get; set; }
        public string? LastError { get; set; }
        public string? GatewayMessageId { get; set; }
        public DateTime? SentAt { get; set; }

        [NotMapped]
        public string? ErrorMessage
        {
            get => LastError;
            set => LastError = value;
        }
    }
}
