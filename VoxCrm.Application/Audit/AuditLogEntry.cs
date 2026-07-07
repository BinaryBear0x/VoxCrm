namespace VoxCrm.Application.Audit;

public sealed record AuditLogEntry
{
    public string Level { get; init; } = AuditLogLevels.Info;
    public string Source { get; init; } = AuditLogSources.Api;
    public string Category { get; init; } = AuditLogCategories.Operation;
    public string Outcome { get; init; } = AuditLogOutcomes.Success;
    public string Action { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public Guid? DealerId { get; init; }
    public Guid? ClinicId { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ActorUserName { get; init; }
    public string? ActorRole { get; init; }
    public string? HttpMethod { get; init; }
    public string? Path { get; init; }
    public int? StatusCode { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? ExceptionType { get; init; }
    public string? TraceId { get; init; }
    public string? CorrelationId { get; init; }
    public long? DurationMs { get; init; }
    public string? ErrorCode { get; init; }
    public string? RequestId { get; init; }
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
}
