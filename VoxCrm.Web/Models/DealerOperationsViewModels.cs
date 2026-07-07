using VoxCrm.Domain.Entities;
using VoxCrm.Web.Services;

namespace VoxCrm.Web.Models;

public class DealerLogsViewModel
{
    public IReadOnlyList<SystemAuditLog> AuditLogs { get; set; } = Array.Empty<SystemAuditLog>();
    public IReadOnlyList<WhatsAppNotification> WhatsAppErrors { get; set; } = Array.Empty<WhatsAppNotification>();
    public IReadOnlyList<Clinic> Clinics { get; set; } = Array.Empty<Clinic>();
    public string? Level { get; set; }
    public string? Source { get; set; }
    public string? Category { get; set; }
    public string? Search { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? ClinicId { get; set; }
}

public class DealerHealthViewModel
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string DatabaseStatus { get; set; } = "unknown";
    public string WebStatus { get; set; } = "ok";
    public string? VoxCrmApiStatus { get; set; }
    public string? VoxCrmApiError { get; set; }
    public GatewayHealthResponse? GatewayHealth { get; set; }
    public string? GatewayError { get; set; }
    public string? GatewayWorkerSummary { get; set; }
    public IReadOnlyList<ContainerStatusItem> Containers { get; set; } = Array.Empty<ContainerStatusItem>();
    public string? ContainerStatusError { get; set; }
    public int ClinicCount { get; set; }
    public int ActiveClinicCount { get; set; }
    public int WhatsAppEnabledClinicCount { get; set; }
    public int PendingWhatsAppCount { get; set; }
    public int FailedWhatsAppCount { get; set; }
    public int NeedsReviewWhatsAppCount { get; set; }
    public DateTime StartedAt { get; set; }
    public long WorkingSetMb { get; set; }
}

public class ContainerStatusItem
{
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Ports { get; set; } = string.Empty;
}
