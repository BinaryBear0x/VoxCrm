using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class WhatsAppTemplate : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }
        public string NotificationType { get; set; } = WhatsAppNotificationTypes.VaccinationReminder;
        public string Body { get; set; } = string.Empty;
    }
}
