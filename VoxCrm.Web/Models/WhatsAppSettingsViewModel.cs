using VoxCrm.Domain.Entities;
using VoxCrm.Web.Services;

namespace VoxCrm.Web.Models;

public class WhatsAppSettingsViewModel
{
    public Clinic Clinic { get; set; } = null!;
    public IReadOnlyList<Clinic> AvailableClinics { get; set; } = Array.Empty<Clinic>();
    public GatewaySessionStatus? SessionStatus { get; set; }
    public GatewayHealthResponse? GatewayHealth { get; set; }
    public GatewayQrResponse? Qr { get; set; }
    public IReadOnlyList<WhatsAppNotification> Notifications { get; set; } = Array.Empty<WhatsAppNotification>();
    public IReadOnlyList<PetOwner> Owners { get; set; } = Array.Empty<PetOwner>();
    public string TemplateBody { get; set; } = string.Empty;
    public string TemplateSample { get; set; } = string.Empty;
    public VoxCrm.Application.Vaccinations.VaccinationReminderPreview? ReminderPreview { get; set; }
    public bool IsDealer { get; set; }
    public string? GatewayWarning { get; set; }
    public DateTime? NextAllowedSendAtUtc { get; set; }
}
public sealed record WhatsAppPreview(WhatsAppNotification Notification, string OwnerName, string? PatientName, string? Error, string? ConfirmationToken = null, bool CanRequeue = false);
