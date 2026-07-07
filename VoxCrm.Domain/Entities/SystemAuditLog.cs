using VoxCrm.Domain.Common;

namespace VoxCrm.Domain.Entities;

public class SystemAuditLog : BaseEntity
{
    public string Level { get; set; } = "Info";
    public string Source { get; set; } = "Web";
    public string Category { get; set; } = "Operation";
    public string Outcome { get; set; } = "Success";
    public string Action { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public Guid? DealerId { get; set; }
    public Guid? ClinicId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorUserName { get; set; }
    public string? ActorRole { get; set; }
    public string? HttpMethod { get; set; }
    public string? Path { get; set; }
    public int? StatusCode { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? ExceptionType { get; set; }
    public string? TraceId { get; set; }
    public string? CorrelationId { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorCode { get; set; }
    public string? MetadataJson { get; set; }
    public string? RequestId { get; set; }
}
