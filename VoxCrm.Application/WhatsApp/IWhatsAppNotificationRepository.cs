namespace VoxCrm.Application.WhatsApp;

public interface IWhatsAppNotificationRepository
{
    Task<IReadOnlyList<ClinicSendWindowInfo>> GetClinicSendWindowsAsync(
        IReadOnlyCollection<Guid> clinicIds,
        CancellationToken cancellationToken);

    Task<int> DeferDueNotificationsAsync(
        Guid clinicId,
        DateTime now,
        DateTime nextAttemptAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WhatsAppClaimedNotification>> ClaimAsync(
        IReadOnlyCollection<Guid> clinicIds,
        DateTime now,
        string gatewayId,
        int batchSize,
        int lockSeconds,
        CancellationToken cancellationToken);

    Task<bool> ReportStatusAsync(
        Guid notificationId,
        WhatsAppStatusRequest request,
        DateTime now,
        CancellationToken cancellationToken);

    Task<int> RecoverExpiredProcessingAsync(DateTime now, CancellationToken cancellationToken);

    Task<Guid> WriteInboundAsync(WhatsAppInboundRequest request, DateTime now, CancellationToken cancellationToken);
}
