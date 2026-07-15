namespace VoxCrm.Application.WhatsApp;

public sealed record WhatsAppClaimRequest(
    IReadOnlyList<Guid> ClinicIds,
    int BatchSize = 10,
    string GatewayId = "voxcrm-whatsapp-gateway",
    int LockSeconds = 180);

public sealed record WhatsAppClaimedNotification(
    Guid NotificationId,
    Guid ClinicId,
    Guid PetOwnerId,
    string PhoneNumber,
    string MessageContent,
    string NotificationType,
    int RetryCount);

public sealed record WhatsAppStatusRequest(
    string Status,
    string? GatewayMessageId,
    string? LastError,
    int? RetryCount,
    DateTime? NextAttemptAt);

public sealed record WhatsAppInboundRequest(
    Guid ClinicId,
    string FromPhone,
    string Message,
    DateTime? ReceivedAt,
    string GatewaySessionId,
    string ProviderMessageId);

public sealed record ExternalAuditLogRequest
{
    public string Level { get; init; } = "Info";
    public string Source { get; init; } = "Gateway";
    public string Category { get; init; } = "WhatsApp";
    public string Outcome { get; init; } = "Success";
    public string Action { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Guid? DealerId { get; init; }
    public Guid? ClinicId { get; init; }
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? ErrorCode { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
}
