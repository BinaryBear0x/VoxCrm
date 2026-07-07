using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.WhatsApp;

public sealed class WhatsAppNotificationRepository : IWhatsAppNotificationRepository
{
    private readonly VoxCrmDbContext _context;

    public WhatsAppNotificationRepository(VoxCrmDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ClinicSendWindowInfo>> GetClinicSendWindowsAsync(
        IReadOnlyCollection<Guid> clinicIds,
        CancellationToken cancellationToken)
    {
        if (clinicIds.Count == 0) return Array.Empty<ClinicSendWindowInfo>();

        return await _context.Clinics
            .AsNoTracking()
            .Where(c => clinicIds.Contains(c.ID) && c.IsActive && c.IsWhatsAppEnabled)
            .Select(c => new ClinicSendWindowInfo(
                c.ID,
                c.Name,
                c.WhatsAppSendWindowEnabled,
                c.WhatsAppSendWindowStart,
                c.WhatsAppSendWindowEnd,
                c.WhatsAppTimeZoneId,
                c.DealerId))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> DeferDueNotificationsAsync(
        Guid clinicId,
        DateTime now,
        DateTime nextAttemptAt,
        CancellationToken cancellationToken)
    {
        return await _context.WhatsAppNotifications
            .IgnoreQueryFilters()
            .Where(n => n.ClinicID == clinicId
                        && WhatsAppNotificationStatuses.Claimable.Contains(n.Status)
                        && (n.NextAttemptAt == null || n.NextAttemptAt <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.NextAttemptAt, nextAttemptAt)
                .SetProperty(n => n.LastError, "Deferred until clinic WhatsApp send window opens."),
                cancellationToken);
    }

    public async Task<IReadOnlyList<WhatsAppClaimedNotification>> ClaimAsync(
        IReadOnlyCollection<Guid> clinicIds,
        DateTime now,
        string gatewayId,
        int batchSize,
        int lockSeconds,
        CancellationToken cancellationToken)
    {
        if (clinicIds.Count == 0) return Array.Empty<WhatsAppClaimedNotification>();

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var claimed = await ClaimNotificationsAsync(
            clinicIds,
            now,
            gatewayId,
            batchSize,
            lockSeconds,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claimed;
    }

    public async Task<bool> ReportStatusAsync(
        Guid notificationId,
        WhatsAppStatusRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var notification = await _context.WhatsAppNotifications.FindAsync(new object?[] { notificationId }, cancellationToken);
        if (notification == null) return false;

        notification.Status = request.Status;
        notification.GatewayMessageId = request.GatewayMessageId ?? notification.GatewayMessageId;
        notification.LastError = request.LastError;
        notification.LockedAt = null;
        notification.LockedBy = null;
        notification.LockExpiresAt = null;

        if (request.RetryCount.HasValue)
            notification.RetryCount = Math.Max(notification.RetryCount, request.RetryCount.Value);

        if (request.Status == WhatsAppNotificationStatuses.Sent)
        {
            notification.SentAt = now;
            notification.NextAttemptAt = null;
            notification.LastError = null;
        }
        else if (request.Status == WhatsAppNotificationStatuses.RetryScheduled)
        {
            notification.NextAttemptAt = request.NextAttemptAt ?? now.AddMinutes(5);
        }
        else
        {
            notification.NextAttemptAt = null;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> RecoverExpiredProcessingAsync(DateTime now, CancellationToken cancellationToken)
    {
        var expired = await _context.WhatsAppNotifications
            .IgnoreQueryFilters()
            .Where(n => n.Status == WhatsAppNotificationStatuses.Processing
                        && n.LockExpiresAt != null
                        && n.LockExpiresAt < now)
            .ToListAsync(cancellationToken);

        foreach (var notification in expired)
        {
            notification.Status = WhatsAppNotificationStatuses.RetryScheduled;
            notification.NextAttemptAt = now.AddMinutes(5);
            notification.LockedAt = null;
            notification.LockedBy = null;
            notification.LockExpiresAt = null;
            notification.LastError = "Processing lock expired before gateway reported a final delivery state.";
        }

        await _context.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    public async Task<Guid> WriteInboundAsync(
        WhatsAppInboundRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var message = new WhatsAppInboundMessage
        {
            ClinicID = request.ClinicId,
            FromPhone = request.FromPhone,
            Message = request.Message,
            ReceivedAt = request.ReceivedAt ?? now,
            GatewaySessionId = request.GatewaySessionId
        };

        _context.WhatsAppInboundMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
        return message.ID;
    }

    private async Task<IReadOnlyList<WhatsAppClaimedNotification>> ClaimNotificationsAsync(
        IReadOnlyCollection<Guid> clinicIds,
        DateTime now,
        string gatewayId,
        int batchSize,
        int lockSeconds,
        CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
UPDATE "WhatsAppNotifications" AS n
SET "Status" = @processing_status,
    "LockedAt" = @locked_at,
    "LockedBy" = @locked_by,
    "LockExpiresAt" = @lock_expires_at,
    "LastError" = NULL
WHERE n."ID" IN (
    SELECT n2."ID"
    FROM "WhatsAppNotifications" AS n2
    INNER JOIN "Clinics" AS c ON c."ID" = n2."ClinicID"
    WHERE n2."ClinicID" = ANY(@clinic_ids)
      AND n2."NotificationType" = ANY(@notification_types)
      AND n2."Status" = ANY(@claimable_statuses)
      AND (n2."NextAttemptAt" IS NULL OR n2."NextAttemptAt" <= @now)
      AND c."IsWhatsAppEnabled" = TRUE
      AND c."IsActive" = TRUE
    ORDER BY COALESCE(n2."NextAttemptAt", n2."CreatedAt"), n2."CreatedAt"
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
)
RETURNING n."ID", n."ClinicID", n."PetOwnerId", n."PhoneNumber", n."MessageContent", n."NotificationType", n."RetryCount";
""";
        AddParameter(command, "processing_status", WhatsAppNotificationStatuses.Processing);
        AddParameter(command, "locked_at", now);
        AddParameter(command, "locked_by", gatewayId);
        AddParameter(command, "lock_expires_at", now.AddSeconds(lockSeconds));
        AddParameter(command, "clinic_ids", clinicIds.ToArray());
        AddParameter(command, "notification_types", GetClaimableNotificationTypes());
        AddParameter(command, "claimable_statuses", WhatsAppNotificationStatuses.Claimable.ToArray());
        AddParameter(command, "now", now);
        AddParameter(command, "batch_size", batchSize);

        var claimed = new List<WhatsAppClaimedNotification>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            claimed.Add(new WhatsAppClaimedNotification(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6)));
        }

        return claimed;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string[] GetClaimableNotificationTypes() =>
    [
        WhatsAppNotificationTypes.VaccinationReminder,
        WhatsAppNotificationTypes.ManualMessage
    ];
}
