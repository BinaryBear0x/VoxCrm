using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.Security;

namespace VoxCrm.Infrastructure.WhatsApp;

public sealed class WhatsAppNotificationRepository : IWhatsAppNotificationRepository
{
    private const int MaxRecoveries = 3;
    private const int MaxLastErrorLength = 1000;

    private readonly VoxCrmDbContext _context;
    private readonly IPiiProtector _protector;

    // A late or stale gateway report must not revive a message that already moved on.
    // Sent is accepted from any non-terminal state because it records a real delivery.
    private static bool IsValidTransition(string current, string requested) => requested switch
    {
        WhatsAppNotificationStatuses.Sent => current is WhatsAppNotificationStatuses.Processing
            or WhatsAppNotificationStatuses.DirectSending
            or WhatsAppNotificationStatuses.RetryScheduled
            or WhatsAppNotificationStatuses.Failed,
        _ => current is WhatsAppNotificationStatuses.Processing or WhatsAppNotificationStatuses.DirectSending
    };

    private static string? Truncate(string? value, int max) =>
        value is { Length: > 0 } && value.Length > max ? value[..max] : value;

    // JSON timestamps with an offset become local DateTime values; timestamptz needs UTC.
    // Past values would retry immediately in a loop and far-future values would strand the message.
    private static DateTime NormalizeNextAttempt(DateTime? requested, DateTime now)
    {
        if (requested is null) return now.AddMinutes(5);
        var utc = requested.Value.Kind switch
        {
            DateTimeKind.Utc => requested.Value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(requested.Value, DateTimeKind.Utc),
            _ => requested.Value.ToUniversalTime()
        };
        var earliest = now.AddMinutes(1);
        var latest = now.AddHours(24);
        return utc < earliest ? earliest : utc > latest ? latest : utc;
    }

    public WhatsAppNotificationRepository(VoxCrmDbContext context, IPiiProtector protector)
    {
        _context = context;
        _protector = protector;
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

    public async Task<int> ReleaseWindowDeferredAsync(Guid clinicId, DateTime now, CancellationToken cancellationToken)
    {
        // Only Pending: a RetryScheduled message has a back-off delay that must be respected.
        return await _context.WhatsAppNotifications
            .IgnoreQueryFilters()
            .Where(n => n.ClinicID == clinicId
                        && n.Status == WhatsAppNotificationStatuses.Pending
                        && n.RetryCount == 0
                        && n.NextAttemptAt != null && n.NextAttemptAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.NextAttemptAt, now)
                .SetProperty(n => n.LastError, (string?)null),
                cancellationToken);
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
        var safe = new List<WhatsAppClaimedNotification>();
        var guard = new WhatsAppRecipientGuard(_context);
        foreach (var item in claimed.Notifications)
        {
            var notification = await _context.WhatsAppNotifications.IgnoreQueryFilters().SingleAsync(n => n.ID == item.NotificationId, cancellationToken);
            string? error;
            try
            {
                error = await guard.CheckAsync(notification, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Corrupt relationship data (for example two primary owners) must stop
                // this one message, not the whole claim batch of every clinic.
                error = "Alıcı ilişkisi doğrulanamadı (tutarsız hasta/sahip kaydı). Gönderim durduruldu.";
            }
            if (error == null) safe.Add(item);
            else
            {
                notification.Status = WhatsAppNotificationStatuses.NeedsReview;
                notification.LastError = error;
                notification.NextAttemptAt = null;
                notification.LockedAt = null; notification.LockedBy = null; notification.LockExpiresAt = null;
            }
        }

        // Rows whose encrypted phone/message could not be read are quarantined one by one.
        // ExecuteUpdate avoids materializing (and failing to decrypt) the same bad row again.
        foreach (var poisonedId in claimed.Undecryptable)
        {
            await _context.WhatsAppNotifications
                .IgnoreQueryFilters()
                .Where(n => n.ID == poisonedId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(n => n.Status, WhatsAppNotificationStatuses.NeedsReview)
                    .SetProperty(n => n.LastError, "Mesaj veya telefon verisi çözülemedi; manuel inceleme gerekli.")
                    .SetProperty(n => n.NextAttemptAt, (DateTime?)null)
                    .SetProperty(n => n.LockedAt, (DateTime?)null)
                    .SetProperty(n => n.LockedBy, (string?)null)
                    .SetProperty(n => n.LockExpiresAt, (DateTime?)null),
                    cancellationToken);
        }
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return safe;
    }

    public async Task<bool> ReportStatusAsync(
        Guid notificationId,
        WhatsAppStatusRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var notification = await _context.WhatsAppNotifications.FindAsync(new object?[] { notificationId }, cancellationToken);
        if (notification == null) return false;
        if (notification.Status == WhatsAppNotificationStatuses.Sent) return request.Status == WhatsAppNotificationStatuses.Sent;
        if (notification.Status is WhatsAppNotificationStatuses.Cancelled or WhatsAppNotificationStatuses.NeedsReview) return false;
        if (!IsValidTransition(notification.Status, request.Status)) return false;

        notification.Status = request.Status;
        notification.GatewayMessageId = request.GatewayMessageId ?? notification.GatewayMessageId;
        notification.LastError = Truncate(request.LastError, MaxLastErrorLength);
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
            notification.NextAttemptAt = NormalizeNextAttempt(request.NextAttemptAt, now);
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
            notification.RetryCount++;
            notification.LockedAt = null;
            notification.LockedBy = null;
            notification.LockExpiresAt = null;

            if (notification.RetryCount > MaxRecoveries)
            {
                // The gateway repeatedly lost the delivery result. The message may already
                // have been delivered, so a human decides instead of risking another send.
                notification.Status = WhatsAppNotificationStatuses.NeedsReview;
                notification.NextAttemptAt = null;
                notification.LastError = "Processing lock expired repeatedly; delivery state is unknown. Manual review required.";
                continue;
            }

            notification.Status = WhatsAppNotificationStatuses.RetryScheduled;
            notification.NextAttemptAt = now.AddMinutes(5);
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
        var existing = await _context.WhatsAppInboundMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.ClinicID == request.ClinicId
                                         && item.ProviderMessageId == request.ProviderMessageId,
                cancellationToken);
        if (existing != null)
            return existing.ID;

        // JSON timestamps with an offset are materialized by ASP.NET as local
        // DateTime values. PostgreSQL timestamptz accepts UTC only.
        var receivedAt = request.ReceivedAt switch
        {
            null => now,
            { Kind: DateTimeKind.Utc } value => value,
            { Kind: DateTimeKind.Unspecified } value => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            { } value => value.ToUniversalTime()
        };

        var message = new WhatsAppInboundMessage
        {
            ClinicID = request.ClinicId,
            FromPhone = request.FromPhone,
            Message = request.Message,
            ReceivedAt = receivedAt,
            GatewaySessionId = request.GatewaySessionId,
            ProviderMessageId = request.ProviderMessageId
        };

        _context.WhatsAppInboundMessages.Add(message);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The gateway delivered the same WhatsApp message twice at the same moment; the
            // other request won. Report the stored row instead of failing.
            _context.Entry(message).State = EntityState.Detached;
            var stored = await _context.WhatsAppInboundMessages
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.ClinicID == request.ClinicId
                                             && item.ProviderMessageId == request.ProviderMessageId,
                    cancellationToken);
            if (stored == null) throw;
            return stored.ID;
        }
        return message.ID;
    }

    public async Task<WhatsAppOptOutResult> ApplyOptOutAsync(
        Guid clinicId,
        IReadOnlyCollection<string> phoneCandidates,
        CancellationToken cancellationToken)
    {
        var lookupKeys = phoneCandidates
            .Select(candidate => _protector.BlindIndex(clinicId, candidate))
            .Where(key => !string.IsNullOrEmpty(key))
            .Select(key => key!)
            .Distinct()
            .ToArray();
        if (lookupKeys.Length == 0) return new WhatsAppOptOutResult(0, 0);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var ownerIds = await _context.PetOwners
            .IgnoreQueryFilters()
            .Where(o => o.ClinicID == clinicId && o.IsActive && o.NormalizedPhone != null && lookupKeys.Contains(o.NormalizedPhone))
            .Select(o => o.ID)
            .ToListAsync(cancellationToken);
        if (ownerIds.Count == 0) return new WhatsAppOptOutResult(0, 0);

        // ExecuteUpdate touches only the consent column: the owner's encrypted fields and
        // lookup hashes are not re-written by this path.
        await _context.PetOwners
            .IgnoreQueryFilters()
            .Where(o => o.ClinicID == clinicId && ownerIds.Contains(o.ID))
            .ExecuteUpdateAsync(setters => setters.SetProperty(o => o.WhatsAppConsent, false), cancellationToken);

        var cancelled = await _context.WhatsAppNotifications
            .IgnoreQueryFilters()
            .Where(n => n.ClinicID == clinicId
                        && ownerIds.Contains(n.PetOwnerId)
                        && WhatsAppNotificationStatuses.Claimable.Contains(n.Status))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.Status, WhatsAppNotificationStatuses.Cancelled)
                .SetProperty(n => n.NextAttemptAt, (DateTime?)null)
                .SetProperty(n => n.LastError, "Müşteri WhatsApp mesajlarını durdurmak istedi."),
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new WhatsAppOptOutResult(ownerIds.Count, cancelled);
    }

    private sealed record ClaimBatch(
        IReadOnlyList<WhatsAppClaimedNotification> Notifications,
        IReadOnlyList<Guid> Undecryptable);

    private async Task<ClaimBatch> ClaimNotificationsAsync(
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
        var undecryptable = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                try
                {
                    claimed.Add(new WhatsAppClaimedNotification(
                        id,
                        reader.GetGuid(1),
                        reader.GetGuid(2),
                        _protector.Unprotect(reader.GetString(3))
                            ?? throw new InvalidOperationException("Claimed WhatsApp phone number could not be decrypted."),
                        _protector.Unprotect(reader.GetString(4))
                            ?? throw new InvalidOperationException("Claimed WhatsApp message could not be decrypted."),
                        reader.GetString(5),
                        reader.GetInt32(6)));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    undecryptable.Add(id);
                }
            }
        }

        return new ClaimBatch(claimed, undecryptable);
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
