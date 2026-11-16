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

    /// <summary>
    /// Messages that never had an attempt keep a future NextAttemptAt only because the send window
    /// was closed when they were queued. When the window is open again they must go now.
    /// </summary>
    Task<int> ReleaseWindowDeferredAsync(Guid clinicId, DateTime now, CancellationToken cancellationToken);

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

    /// <summary>
    /// Withdraws WhatsApp consent of the clinic's active customers matching any of the phone
    /// spellings and cancels their queued messages.
    /// </summary>
    Task<WhatsAppOptOutResult> ApplyOptOutAsync(
        Guid clinicId,
        IReadOnlyCollection<string> phoneCandidates,
        CancellationToken cancellationToken);
}
