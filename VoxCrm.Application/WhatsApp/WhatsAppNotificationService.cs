using VoxCrm.Application.Audit;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.WhatsApp;

public interface IWhatsAppNotificationService
{
    Task<IReadOnlyList<WhatsAppClaimedNotification>> ClaimAsync(
        WhatsAppClaimRequest request,
        string gatewayId,
        DateTime now,
        CancellationToken cancellationToken);

    Task<bool> ReportStatusAsync(Guid notificationId, WhatsAppStatusRequest request, DateTime now, CancellationToken cancellationToken);
    Task<int> RecoverExpiredProcessingAsync(DateTime now, CancellationToken cancellationToken);
    Task<Guid> WriteInboundAsync(WhatsAppInboundRequest request, DateTime now, CancellationToken cancellationToken);
}

public sealed class WhatsAppNotificationService : IWhatsAppNotificationService
{
    private readonly IWhatsAppNotificationRepository _repository;
    private readonly IClinicSendWindowCalculator _sendWindowCalculator;
    private readonly IAuditLogger _auditLogger;

    public WhatsAppNotificationService(
        IWhatsAppNotificationRepository repository,
        IClinicSendWindowCalculator sendWindowCalculator,
        IAuditLogger auditLogger)
    {
        _repository = repository;
        _sendWindowCalculator = sendWindowCalculator;
        _auditLogger = auditLogger;
    }

    public async Task<IReadOnlyList<WhatsAppClaimedNotification>> ClaimAsync(
        WhatsAppClaimRequest request,
        string gatewayId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var clinicIds = request.ClinicIds.Distinct().ToArray();
        if (clinicIds.Length == 0) return Array.Empty<WhatsAppClaimedNotification>();

        var batchSize = Math.Clamp(request.BatchSize, 1, 50);
        var lockSeconds = Math.Clamp(request.LockSeconds, 30, 600);
        var windows = await _repository.GetClinicSendWindowsAsync(clinicIds, cancellationToken);

        var openClinicIds = new List<Guid>();
        foreach (var clinic in windows)
        {
            var decision = _sendWindowCalculator.GetDecision(clinic, now);
            if (decision.IsOpen)
            {
                openClinicIds.Add(clinic.ClinicId);
                continue;
            }

            var deferredCount = await _repository.DeferDueNotificationsAsync(
                clinic.ClinicId,
                now,
                decision.NextAllowedUtc,
                cancellationToken);

            if (deferredCount > 0)
            {
                await _auditLogger.LogAsync(new AuditLogEntry
                {
                    Level = AuditLogLevels.Warning,
                    Source = AuditLogSources.Api,
                    Category = AuditLogCategories.WhatsApp,
                    Outcome = AuditLogOutcomes.Deferred,
                    Action = "WhatsAppNotifications.DeferOutsideSendWindow",
                    Message = $"{deferredCount} WhatsApp bildirimi klinik gönderim penceresi dışında olduğu için ertelendi.",
                    ClinicId = clinic.ClinicId,
                    DealerId = clinic.DealerId,
                    EntityType = "Clinic",
                    EntityId = clinic.ClinicId.ToString(),
                    Metadata = new Dictionary<string, object?>
                    {
                        ["clinicName"] = clinic.ClinicName,
                        ["deferredCount"] = deferredCount,
                        ["nextAttemptAtUtc"] = decision.NextAllowedUtc,
                        ["timeZone"] = decision.TimeZoneId
                    }
                }, cancellationToken);
            }
        }

        if (openClinicIds.Count == 0)
            return Array.Empty<WhatsAppClaimedNotification>();

        var claimed = await _repository.ClaimAsync(
            openClinicIds,
            now,
            gatewayId,
            batchSize,
            lockSeconds,
            cancellationToken);

        await _auditLogger.LogAsync(new AuditLogEntry
        {
            Level = AuditLogLevels.Info,
            Source = AuditLogSources.Api,
            Category = AuditLogCategories.WhatsApp,
            Outcome = AuditLogOutcomes.Success,
            Action = "WhatsAppNotifications.Claim",
            Message = $"{claimed.Count} WhatsApp bildirimi gateway tarafından claim edildi.",
            Metadata = new Dictionary<string, object?>
            {
                ["claimedCount"] = claimed.Count,
                ["requestedClinicCount"] = clinicIds.Length,
                ["openClinicCount"] = openClinicIds.Count,
                ["gatewayId"] = gatewayId
            }
        }, cancellationToken);

        return claimed;
    }

    public async Task<bool> ReportStatusAsync(
        Guid notificationId,
        WhatsAppStatusRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var updated = await _repository.ReportStatusAsync(notificationId, request, now, cancellationToken);
        await _auditLogger.LogAsync(new AuditLogEntry
        {
            Level = request.Status is WhatsAppNotificationStatuses.Failed or WhatsAppNotificationStatuses.NeedsReview
                ? AuditLogLevels.Error
                : request.Status == WhatsAppNotificationStatuses.RetryScheduled ? AuditLogLevels.Warning : AuditLogLevels.Info,
            Source = AuditLogSources.Api,
            Category = AuditLogCategories.WhatsApp,
            Outcome = request.Status == WhatsAppNotificationStatuses.RetryScheduled
                ? AuditLogOutcomes.RetryScheduled
                : request.Status is WhatsAppNotificationStatuses.Failed or WhatsAppNotificationStatuses.NeedsReview
                    ? AuditLogOutcomes.Failed
                    : AuditLogOutcomes.Success,
            Action = "WhatsAppNotifications.ReportStatus",
            Message = updated
                ? $"WhatsApp bildirimi {request.Status} durumuna alindi."
                : "WhatsApp durum guncellemesi icin bildirim bulunamadi.",
            EntityType = "WhatsAppNotification",
            EntityId = notificationId.ToString(),
            ErrorCode = request.Status,
            Metadata = new Dictionary<string, object?>
            {
                ["status"] = request.Status,
                ["gatewayMessageId"] = request.GatewayMessageId,
                ["retryCount"] = request.RetryCount,
                ["lastError"] = request.LastError
            }
        }, cancellationToken);

        return updated;
    }

    public async Task<int> RecoverExpiredProcessingAsync(DateTime now, CancellationToken cancellationToken)
    {
        var recovered = await _repository.RecoverExpiredProcessingAsync(now, cancellationToken);
        if (recovered > 0)
        {
            await _auditLogger.LogAsync(new AuditLogEntry
            {
                Level = AuditLogLevels.Warning,
                Source = AuditLogSources.Api,
                Category = AuditLogCategories.WhatsApp,
                Outcome = AuditLogOutcomes.RetryScheduled,
                Action = "WhatsAppNotifications.RecoverExpiredProcessing",
                Message = $"{recovered} Processing WhatsApp kilidi zaman asimi nedeniyle RetryScheduled yapildi.",
                Metadata = new Dictionary<string, object?> { ["recovered"] = recovered }
            }, cancellationToken);
        }

        return recovered;
    }

    public async Task<Guid> WriteInboundAsync(
        WhatsAppInboundRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var inboundId = await _repository.WriteInboundAsync(request, now, cancellationToken);
        await _auditLogger.LogAsync(new AuditLogEntry
        {
            Level = AuditLogLevels.Info,
            Source = AuditLogSources.Api,
            Category = AuditLogCategories.WhatsApp,
            Outcome = AuditLogOutcomes.Success,
            Action = "WhatsAppInbound.Write",
            Message = "WhatsApp inbound mesaj kaydedildi.",
            ClinicId = request.ClinicId,
            EntityType = "WhatsAppInboundMessage",
            EntityId = inboundId.ToString(),
            Metadata = new Dictionary<string, object?>
            {
                ["fromPhone"] = request.FromPhone,
                ["message"] = request.Message,
                ["gatewaySessionId"] = request.GatewaySessionId
            }
        }, cancellationToken);
        return inboundId;
    }
}
