using VoxCrm.Domain.Entities;
using VoxCrm.Web.Services;

namespace VoxCrm.Web.Models;

public class WhatsAppSettingsViewModel
{
    public Clinic Clinic { get; set; } = null!;
    public IReadOnlyList<Clinic> AvailableClinics { get; set; } = Array.Empty<Clinic>();
    public WhatsAppTemplate Template { get; set; } = new();
    public GatewaySessionStatus? SessionStatus { get; set; }
    public GatewayHealthResponse? GatewayHealth { get; set; }
    public GatewayQrResponse? Qr { get; set; }
    public IReadOnlyList<WhatsAppNotification> Notifications { get; set; } = Array.Empty<WhatsAppNotification>();
    public IReadOnlyList<WhatsAppInboundMessage> InboundMessages { get; set; } = Array.Empty<WhatsAppInboundMessage>();
    public bool IsDealer { get; set; }
    public string? GatewayWarning { get; set; }
    public DateTime? NextAllowedSendAtUtc { get; set; }
}
