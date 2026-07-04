using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities
{
    public class WhatsAppInboundMessage : BaseEntity, ITenantEntity
    {
        public Guid ClinicID { get; set; }
        public string FromPhone { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; }
        public string GatewaySessionId { get; set; } = string.Empty;
    }
}
