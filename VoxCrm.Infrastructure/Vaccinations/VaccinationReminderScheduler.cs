using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VoxCrm.Application.Vaccinations;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Infrastructure.WhatsApp;

namespace VoxCrm.Infrastructure.Vaccinations;

public sealed class VaccinationReminderScheduler : IVaccinationReminderScheduler, IVaccinationReminderForecast
{
    private readonly VoxCrmDbContext _context;
    private readonly IClinicSendWindowCalculator _sendWindowCalculator;
    private readonly ILogger<VaccinationReminderScheduler>? _logger;

    public VaccinationReminderScheduler(
        VoxCrmDbContext context,
        IClinicSendWindowCalculator sendWindowCalculator,
        ILogger<VaccinationReminderScheduler>? logger = null)
    {
        _context = context;
        _sendWindowCalculator = sendWindowCalculator;
        _logger = logger;
    }

    // Locks are held until the transaction ends, so the chunk size also bounds how many
    // advisory locks one transaction owns (PostgreSQL's shared lock table is finite).
    private const int ReconcileChunkSize = 100;

    public async Task<int> ReconcileAsync(
        Guid? vaccinationRecordId = null,
        DateTime? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.SpecifyKind(utcNow ?? DateTime.UtcNow, DateTimeKind.Utc);
        List<Guid> candidateIds;
        if (vaccinationRecordId.HasValue)
        {
            candidateIds = await _context.VaccinationRecords.AsNoTracking()
                .Where(record => record.ID == vaccinationRecordId.Value)
                .Select(record => record.ID)
                .ToListAsync(cancellationToken);
        }
        else
        {
            candidateIds = await FindBulkCandidatesAsync(now, cancellationToken);
        }

        if (candidateIds.Count == 0)
            return 0;

        // A single record, or a caller that already owns a transaction, is reconciled as one
        // unit and its errors propagate. The daily bulk run isolates failures instead.
        if (vaccinationRecordId.HasValue || _context.Database.CurrentTransaction != null)
        {
            var queued = 0;
            await using var transaction = _context.Database.CurrentTransaction == null
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            queued += await ReconcileChunkAsync(candidateIds, now, cancellationToken);
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return queued;
        }

        var queuedCount = 0;
        var failedRecords = 0;
        foreach (var chunk in candidateIds.Chunk(ReconcileChunkSize))
        {
            try
            {
                queuedCount += await ReconcileInTransactionAsync(chunk, now, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad record (for example a unique-index race with a user request) must not
                // roll back or block every other clinic's reminders: retry the chunk record by record.
                _logger?.LogWarning(ex, "Vaccination reminder chunk failed; retrying its {Count} records one by one", chunk.Length);
                _context.ChangeTracker.Clear();
                foreach (var recordId in chunk)
                {
                    try
                    {
                        queuedCount += await ReconcileInTransactionAsync(new[] { recordId }, now, cancellationToken);
                    }
                    catch (Exception single) when (single is not OperationCanceledException)
                    {
                        failedRecords++;
                        _logger?.LogError(single, "Vaccination reminder could not be reconciled for record {RecordId}", recordId);
                        _context.ChangeTracker.Clear();
                    }
                }
            }
            _context.ChangeTracker.Clear();
        }

        // Surface the problem to the job runner (retry, dashboard) after everything else is done.
        if (failedRecords > 0)
            throw new InvalidOperationException($"{failedRecords} aşı hatırlatma kaydı işlenemedi; diğer kayıtlar işlendi.");
        return queuedCount;
    }

    private async Task<int> ReconcileInTransactionAsync(IReadOnlyList<Guid> recordIds, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var queued = await ReconcileChunkAsync(recordIds, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return queued;
    }

    // Records that can still need action: due recently or soon, plus any record whose queued
    // reminder may have to be cancelled. Records due years from now (their reminder day has
    // not arrived) are no longer loaded one by one every day.
    private async Task<List<Guid>> FindBulkCandidatesAsync(DateTime now, CancellationToken cancellationToken)
    {
        // Include one overdue day so a queued reminder can be cancelled when
        // its due date passes. Older historical records never trigger sends.
        var oldestRelevantDate = now.Date.AddDays(-1);
        var longestReminder = await _context.VaccineTypes.AsNoTracking()
            .Select(type => (int?)type.ReminderDaysBefore)
            .MaxAsync(cancellationToken) ?? 1;
        // +2 days of slack covers clinics whose local date is ahead of UTC.
        var latestRelevantDate = now.Date.AddDays(Math.Max(1, longestReminder) + 2);

        return await _context.VaccinationRecords.AsNoTracking()
            .Where(record => record.IsActive
                             && record.NextDueDate >= oldestRelevantDate
                             && (record.NextDueDate <= latestRelevantDate
                                 || _context.WhatsAppNotifications.Any(notification =>
                                     notification.VaccinationRecordId == record.ID
                                     && (notification.Status == WhatsAppNotificationStatuses.Pending
                                         || notification.Status == WhatsAppNotificationStatuses.RetryScheduled))))
            .Select(record => record.ID)
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);
    }

    private async Task<int> ReconcileChunkAsync(IReadOnlyList<Guid> recordIds, DateTime now, CancellationToken cancellationToken)
    {
        // The same record may be reconciled by a user request and Hangfire at
        // the same time. A transaction-scoped lock plus the unique index keeps
        // notification creation idempotent across processes. Locks are taken first, in a
        // stable order, and the data is read afterwards so it cannot be stale.
        foreach (var recordId in recordIds.OrderBy(id => id))
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({recordId.ToString()}, 0))",
                cancellationToken);
        }

        var records = await _context.VaccinationRecords
            .Include(v => v.Patient)
                .ThenInclude(patient => patient.Owners)
                .ThenInclude(ownership => ownership.PetOwner)
            .Include(v => v.VaccineType)
            .Where(v => recordIds.Contains(v.ID))
            .ToListAsync(cancellationToken);
        if (records.Count == 0)
            return 0;

        var notifications = await _context.WhatsAppNotifications
            .Where(notification => notification.VaccinationRecordId != null
                                   && recordIds.Contains(notification.VaccinationRecordId.Value)
                                   && notification.NotificationType == WhatsAppNotificationTypes.VaccinationReminder)
            .ToListAsync(cancellationToken);
        var notificationByRecord = notifications.ToDictionary(notification => notification.VaccinationRecordId!.Value);

        var clinicIds = records.Select(record => record.ClinicID).Distinct().ToList();
        var clinics = await _context.Clinics.AsNoTracking()
            .Where(c => clinicIds.Contains(c.ID))
            .ToDictionaryAsync(c => c.ID, cancellationToken);
        var templateBodies = (await _context.WhatsAppTemplates.AsNoTracking()
                .Where(t => clinicIds.Contains(t.ClinicID)
                            && t.NotificationType == WhatsAppNotificationTypes.VaccinationReminder
                            && t.IsActive)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(cancellationToken))
            .GroupBy(t => t.ClinicID)
            .ToDictionary(group => group.Key, group => group.First().Body);

        var queuedCount = 0;
        foreach (var record in records.OrderBy(record => record.ID))
        {
            notificationByRecord.TryGetValue(record.ID, out var existing);
            clinics.TryGetValue(record.ClinicID, out var clinic);
            var localToday = GetLocalDate(clinic?.WhatsAppTimeZoneId, now);
            var reminderDays = record.VaccineType.ReminderDaysBefore <= 0
                ? 1
                : record.VaccineType.ReminderDaysBefore;
            var reminderDate = record.NextDueDate.Date.AddDays(-reminderDays);
            var renewed = await IsRenewedAsync(record, cancellationToken);
            var isEligible = record.IsActive && !renewed
                             && record.NextDueDate.Date >= localToday
                             && localToday >= reminderDate;

            var owner = record.Patient.Owners
                .FirstOrDefault(ownership => ownership.IsActive && ownership.IsPrimaryOwner && ownership.PetOwner.IsActive)
                ?.PetOwner;
            var canSend = isEligible
                          && record.Patient.IsActive
                          && clinic is { IsActive: true, IsWhatsAppEnabled: true, AutomaticVaccinationRemindersEnabled: true }
                          && owner is { WhatsAppConsent: true }
                          && !string.IsNullOrWhiteSpace(owner.Phone);

            if (!canSend)
            {
                CancelClaimable(existing);
                if (existing == null || IsClaimable(existing.Status))
                    record.IsReminderSent = false;
                continue;
            }

            templateBodies.TryGetValue(record.ClinicID, out var templateBody);
            var message = Render(templateBody, owner!, record, clinic!);
            var nextAttemptAt = _sendWindowCalculator.GetNextAllowedSendUtc(clinic!, now);

            if (existing == null)
            {
                _context.WhatsAppNotifications.Add(new WhatsAppNotification
                {
                    ClinicID = record.ClinicID,
                    PetOwnerId = owner!.ID,
                    VaccinationRecordId = record.ID,
                    PatientId = record.PatientId,
                    IsAutomatic = true,
                    RecipientSnapshot = WhatsAppRecipientGuard.Snapshot(owner!, record.Patient, record),
                    PhoneNumber = owner.Phone!,
                    NotificationType = WhatsAppNotificationTypes.VaccinationReminder,
                    MessageContent = message,
                    Status = WhatsAppNotificationStatuses.Pending,
                    NextAttemptAt = nextAttemptAt,
                });
                queuedCount++;
            }
            else if (existing.Status is WhatsAppNotificationStatuses.Pending
                     or WhatsAppNotificationStatuses.RetryScheduled)
            {
                if (!WhatsAppRecipientGuard.SnapshotMatches(existing.RecipientSnapshot, owner!, record.Patient, record))
                {
                    existing.Status = WhatsAppNotificationStatuses.NeedsReview;
                    existing.LastError = "Alıcı bilgileri değişti. Gönderim durduruldu; önizlemeden kontrol edin.";
                    existing.NextAttemptAt = null;
                    continue;
                }
                existing.PetOwnerId = owner!.ID;
                existing.PhoneNumber = owner.Phone!;
                existing.MessageContent = message;
                if (existing.Status != WhatsAppNotificationStatuses.RetryScheduled)
                {
                    existing.Status = WhatsAppNotificationStatuses.Pending;
                    existing.NextAttemptAt = nextAttemptAt;
                    existing.RetryCount = 0;
                    existing.LastError = null;
                    existing.LockedAt = null;
                    existing.LockedBy = null;
                    existing.LockExpiresAt = null;
                }
            }

            record.IsReminderSent = true;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return queuedCount;
    }

    public async Task<VaccinationReminderActionResult> RetryFailedAsync(
        Guid vaccinationRecordId,
        DateTime? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.SpecifyKind(utcNow ?? DateTime.UtcNow, DateTimeKind.Utc);
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await AcquireRecordLockAsync(vaccinationRecordId, cancellationToken);

        var record = await LoadRecordAsync(vaccinationRecordId, cancellationToken);
        if (record == null)
            return new VaccinationReminderActionResult(false, "Aşı kaydı bulunamadı.");

        var notification = await FindNotificationAsync(vaccinationRecordId, cancellationToken);
        if (notification?.Status != WhatsAppNotificationStatuses.Failed)
            return new VaccinationReminderActionResult(false, "Yalnız başarısız bildirimler yeniden kuyruğa alınabilir.");

        var dispatch = await BuildDispatchAsync(record, now, cancellationToken);
        if (!dispatch.Succeeded)
            return new VaccinationReminderActionResult(false, dispatch.Error);

        notification.IsAutomatic = false;
        notification.PatientId = record.PatientId;
        notification.RecipientSnapshot = WhatsAppRecipientGuard.Snapshot(dispatch.Owner!, record.Patient, record);
        notification.PetOwnerId = dispatch.Owner!.ID;
        notification.PhoneNumber = dispatch.Owner.Phone!;
        notification.MessageContent = dispatch.Message!;
        notification.Status = WhatsAppNotificationStatuses.Pending;
        notification.RetryCount = 0;
        notification.LastError = null;
        notification.GatewayMessageId = null;
        notification.SentAt = null;
        notification.NextAttemptAt = _sendWindowCalculator.GetNextAllowedSendUtc(dispatch.Clinic!, now);
        ClearLock(notification);
        record.IsReminderSent = true;

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new VaccinationReminderActionResult(true);
    }

    public async Task<VaccinationReminderDispatchResult> StartImmediateAsync(
        Guid vaccinationRecordId,
        DateTime? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.SpecifyKind(utcNow ?? DateTime.UtcNow, DateTimeKind.Utc);
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await AcquireRecordLockAsync(vaccinationRecordId, cancellationToken);

        var record = await LoadRecordAsync(vaccinationRecordId, cancellationToken);
        if (record == null)
            return new VaccinationReminderDispatchResult(false, Error: "Aşı kaydı bulunamadı.");

        var dispatch = await BuildDispatchAsync(record, now, cancellationToken);
        if (!dispatch.Succeeded)
            return new VaccinationReminderDispatchResult(false, Error: dispatch.Error);

        var notification = await FindNotificationAsync(vaccinationRecordId, cancellationToken);
        if (notification?.Status == WhatsAppNotificationStatuses.Sent)
            return new VaccinationReminderDispatchResult(false, Error: "Bu aşı hatırlatması daha önce gönderilmiş.");
        if (notification?.Status == WhatsAppNotificationStatuses.Processing)
            return new VaccinationReminderDispatchResult(false, Error: "Bildirim şu anda kuyruk tarafından işleniyor.");
        if (notification?.Status == WhatsAppNotificationStatuses.DirectSending
            && notification.LockExpiresAt > now)
        {
            return new VaccinationReminderDispatchResult(false, Error: "Anında gönderim zaten devam ediyor.");
        }

        if (notification == null)
        {
            notification = new WhatsAppNotification
            {
                ClinicID = record.ClinicID,
                VaccinationRecordId = record.ID,
                NotificationType = WhatsAppNotificationTypes.VaccinationReminder,
            };
            _context.WhatsAppNotifications.Add(notification);
        }

        notification.IsAutomatic = false;
        notification.PatientId = record.PatientId;
        notification.RecipientSnapshot = WhatsAppRecipientGuard.Snapshot(dispatch.Owner!, record.Patient, record);
        notification.PetOwnerId = dispatch.Owner!.ID;
        notification.PhoneNumber = dispatch.Owner.Phone!;
        notification.MessageContent = dispatch.Message!;
        notification.Status = WhatsAppNotificationStatuses.DirectSending;
        notification.RetryCount = 0;
        notification.LastError = null;
        notification.GatewayMessageId = null;
        notification.SentAt = null;
        notification.NextAttemptAt = null;
        notification.LockedAt = now;
        notification.LockedBy = "direct-send";
        notification.LockExpiresAt = now.AddMinutes(2);
        record.IsReminderSent = true;

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new VaccinationReminderDispatchResult(
            true,
            dispatch.Clinic!.ID,
            notification.ID,
            dispatch.Owner.Phone,
            dispatch.Message);
    }

    public async Task CompleteImmediateAsync(
        Guid vaccinationRecordId,
        string? gatewayMessageId,
        DateTime? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.SpecifyKind(utcNow ?? DateTime.UtcNow, DateTimeKind.Utc);
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await AcquireRecordLockAsync(vaccinationRecordId, cancellationToken);
        var notification = await FindNotificationAsync(vaccinationRecordId, cancellationToken);
        if (notification == null)
            throw new InvalidOperationException("Anında gönderim kaydı bulunamadı.");

        if (notification.Status != WhatsAppNotificationStatuses.Sent)
        {
            notification.Status = WhatsAppNotificationStatuses.Sent;
            notification.GatewayMessageId = gatewayMessageId;
            notification.SentAt = now;
            notification.NextAttemptAt = null;
            notification.LastError = null;
            ClearLock(notification);
            await _context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task FailImmediateAsync(
        Guid vaccinationRecordId,
        string error,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await AcquireRecordLockAsync(vaccinationRecordId, cancellationToken);
        var notification = await FindNotificationAsync(vaccinationRecordId, cancellationToken);
        if (notification?.Status == WhatsAppNotificationStatuses.DirectSending)
        {
            notification.Status = WhatsAppNotificationStatuses.Failed;
            notification.LastError = string.IsNullOrWhiteSpace(error)
                ? "Anında WhatsApp gönderimi başarısız oldu."
                : error.Trim()[..Math.Min(error.Trim().Length, 1000)];
            notification.NextAttemptAt = null;
            ClearLock(notification);
            await _context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // Keep the rules below in step with ReconcileChunkAsync: this forecast must answer "what would the
    // automatic run queue?" with the same eligibility, minus the clinic's on/off switch.
    public async Task<VaccinationReminderPreview> PreviewAsync(
        Guid clinicId,
        DateTime? utcNow = null,
        int horizonDays = 30,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.SpecifyKind(utcNow ?? DateTime.UtcNow, DateTimeKind.Utc);
        var empty = new VaccinationReminderPreview(Array.Empty<VaccinationReminderPreviewItem>(), 0, 0, 0, 0, 0);
        var clinic = await _context.Clinics.AsNoTracking().SingleOrDefaultAsync(c => c.ID == clinicId, cancellationToken);
        if (clinic is not { IsActive: true, IsWhatsAppEnabled: true })
            return empty;

        var today = GetLocalDate(clinic.WhatsAppTimeZoneId, now);
        var horizon = today.AddDays(Math.Max(1, horizonDays));
        var longestReminder = await _context.VaccineTypes.IgnoreQueryFilters().AsNoTracking()
            .Where(type => type.ClinicID == clinicId)
            .Select(type => (int?)type.ReminderDaysBefore)
            .MaxAsync(cancellationToken) ?? 1;

        // timestamptz parameters must be UTC-kind; the local calendar dates are only labels here.
        var queryFrom = DateTime.SpecifyKind(today, DateTimeKind.Utc);
        var queryTo = DateTime.SpecifyKind(horizon.AddDays(Math.Max(1, longestReminder)), DateTimeKind.Utc);
        var records = await _context.VaccinationRecords.IgnoreQueryFilters().AsNoTracking()
            .Include(v => v.Patient).ThenInclude(patient => patient.Owners).ThenInclude(ownership => ownership.PetOwner)
            .Include(v => v.VaccineType)
            .Where(v => v.ClinicID == clinicId && v.IsActive && v.NextDueDate >= queryFrom && v.NextDueDate <= queryTo)
            .OrderBy(v => v.NextDueDate)
            .ToListAsync(cancellationToken);
        if (records.Count == 0)
            return empty;

        // A newer application of the same vaccine to the same animal replaces the reminder.
        var newestApplication = (await _context.VaccinationRecords.IgnoreQueryFilters().AsNoTracking()
                .Where(v => v.ClinicID == clinicId && v.IsActive)
                .Select(v => new { v.PatientId, v.VaccineTypeId, v.AdministeredDate })
                .ToListAsync(cancellationToken))
            .GroupBy(v => (v.PatientId, v.VaccineTypeId))
            .ToDictionary(group => group.Key, group => group.Max(v => v.AdministeredDate.Date));

        var recordIds = records.Select(r => r.ID).ToList();
        var alreadyQueuedIds = (await _context.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.ClinicID == clinicId && n.VaccinationRecordId != null && recordIds.Contains(n.VaccinationRecordId.Value)
                            && n.NotificationType == WhatsAppNotificationTypes.VaccinationReminder)
                .Select(n => n.VaccinationRecordId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var templateBody = await _context.WhatsAppTemplates.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.ClinicID == clinicId && t.IsActive && t.NotificationType == WhatsAppNotificationTypes.VaccinationReminder)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.Body)
            .FirstOrDefaultAsync(cancellationToken);

        var items = new List<VaccinationReminderPreviewItem>();
        int alreadyQueued = 0, noOwner = 0, noConsent = 0, noPhone = 0, eligible = 0;
        foreach (var record in records)
        {
            var reminderDays = record.VaccineType.ReminderDaysBefore <= 0 ? 1 : record.VaccineType.ReminderDaysBefore;
            var reminderDate = record.NextDueDate.Date.AddDays(-reminderDays);
            if (reminderDate > horizon || !record.Patient.IsActive)
                continue;
            if (newestApplication.TryGetValue((record.PatientId, record.VaccineTypeId), out var newest) && newest > record.AdministeredDate.Date)
                continue;
            if (alreadyQueuedIds.Contains(record.ID)) { alreadyQueued++; continue; }

            var owner = record.Patient.Owners
                .FirstOrDefault(ownership => ownership.IsActive && ownership.IsPrimaryOwner && ownership.PetOwner.IsActive)
                ?.PetOwner;
            if (owner == null) { noOwner++; continue; }
            if (!owner.WhatsAppConsent) { noConsent++; continue; }
            if (string.IsNullOrWhiteSpace(owner.Phone)) { noPhone++; continue; }

            eligible++;
            if (items.Count < 100)
            {
                items.Add(new VaccinationReminderPreviewItem(
                    record.ID,
                    $"{owner.FirstName} {owner.LastName}".Trim(),
                    record.Patient.Name,
                    record.VaccineType.Name,
                    record.NextDueDate.Date,
                    reminderDate > today ? reminderDate : today,
                    reminderDate <= today,
                    Render(templateBody, owner, record, clinic)));
            }
        }

        return new VaccinationReminderPreview(
            items.OrderBy(item => item.PlannedDate).ThenBy(item => item.DueDate).ToList(),
            eligible, alreadyQueued, noOwner, noConsent, noPhone);
    }

    private static void CancelClaimable(WhatsAppNotification? notification)
    {
        if (notification == null || !IsClaimable(notification.Status))
            return;

        notification.Status = WhatsAppNotificationStatuses.Cancelled;
        notification.NextAttemptAt = null;
        ClearLock(notification);
        notification.LastError = null;
    }

    private Task AcquireRecordLockAsync(Guid recordId, CancellationToken cancellationToken) =>
        _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({recordId.ToString()}, 0))",
            cancellationToken);

    private Task<VaccinationRecord?> LoadRecordAsync(Guid recordId, CancellationToken cancellationToken) =>
        _context.VaccinationRecords
            .Include(v => v.Patient)
                .ThenInclude(patient => patient.Owners)
                .ThenInclude(ownership => ownership.PetOwner)
            .Include(v => v.VaccineType)
            .SingleOrDefaultAsync(v => v.ID == recordId, cancellationToken);

    private Task<WhatsAppNotification?> FindNotificationAsync(Guid recordId, CancellationToken cancellationToken) =>
        _context.WhatsAppNotifications.SingleOrDefaultAsync(
            notification => notification.VaccinationRecordId == recordId
                            && notification.NotificationType == WhatsAppNotificationTypes.VaccinationReminder,
            cancellationToken);

    private async Task<DispatchContext> BuildDispatchAsync(
        VaccinationRecord record,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var clinic = await _context.Clinics.SingleOrDefaultAsync(c => c.ID == record.ClinicID, cancellationToken);
        var localToday = GetLocalDate(clinic?.WhatsAppTimeZoneId, utcNow);
        if (await IsRenewedAsync(record, cancellationToken))
            return new DispatchContext(false, Error: "Bu aşı türü için daha yeni uygulama var. Güncel kaydı kullanın.");
        if (!record.IsActive || !record.Patient.IsActive || record.NextDueDate.Date < localToday)
            return new DispatchContext(false, Error: "Geçmiş veya arşivlenmiş aşı için otomatik bildirim gönderilemez.");
        if (clinic is not { IsActive: true, IsWhatsAppEnabled: true, ManualWhatsAppEnabled: true })
            return new DispatchContext(false, Error: "Klinik WhatsApp gönderimi aktif değil.");

        var owner = record.Patient.Owners.FirstOrDefault(ownership => ownership.IsActive && ownership.IsPrimaryOwner && ownership.PetOwner.IsActive)?.PetOwner;
        if (owner is not { WhatsAppConsent: true } || string.IsNullOrWhiteSpace(owner.Phone))
            return new DispatchContext(false, Error: "Birincil sahibin WhatsApp izni ve telefon numarası bulunmalıdır.");

        var template = await _context.WhatsAppTemplates
            .Where(t => t.ClinicID == record.ClinicID
                        && t.NotificationType == WhatsAppNotificationTypes.VaccinationReminder
                        && t.IsActive)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return new DispatchContext(true, clinic, owner, Render(template?.Body, owner, record, clinic));
    }

    // A newer application of the same vaccine to the same animal replaces this reminder.
    private Task<bool> IsRenewedAsync(VaccinationRecord record, CancellationToken cancellationToken) =>
        _context.VaccinationRecords.AnyAsync(x => x.ClinicID == record.ClinicID && x.IsActive && x.PatientId == record.PatientId
            && x.VaccineTypeId == record.VaccineTypeId && x.AdministeredDate.Date > record.AdministeredDate.Date, cancellationToken);

    private static void ClearLock(WhatsAppNotification notification)
    {
        notification.LockedAt = null;
        notification.LockedBy = null;
        notification.LockExpiresAt = null;
    }

    private static bool IsClaimable(string status) =>
        status is WhatsAppNotificationStatuses.Pending
            or WhatsAppNotificationStatuses.RetryScheduled
            or WhatsAppNotificationStatuses.Cancelled;

    private static DateTime GetLocalDate(string? timeZoneId, DateTime utcNow) =>
        VoxCrm.Domain.Common.ClinicTime.Today(timeZoneId?.Trim(), new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)))
            .ToDateTime(TimeOnly.MinValue);

    private static string Render(
        string? templateBody,
        PetOwner owner,
        VaccinationRecord record,
        Clinic clinic) =>
        VaccinationMessageTemplate.Render(templateBody, new VaccinationMessageValues(
            owner.FirstName, owner.LastName, record.Patient.Name, record.VaccineType.Name, record.NextDueDate, clinic.Name));

    private sealed record DispatchContext(
        bool Succeeded,
        Clinic? Clinic = null,
        PetOwner? Owner = null,
        string? Message = null,
        string? Error = null);
}
