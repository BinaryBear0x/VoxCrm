using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.DealerOperations;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.DealerOperations;

public sealed class DealerLogService : IDealerLogService
{
    private readonly VoxCrmDbContext _context;

    public DealerLogService(VoxCrmDbContext context) => _context = context;

    public async Task<DealerLogResult> GetAsync(DealerLogQuery query, CancellationToken cancellationToken = default)
    {
        var clinics = await _context.Clinics.AsNoTracking()
            .Where(clinic => clinic.DealerId == query.DealerId)
            .OrderBy(clinic => clinic.Name)
            .ToListAsync(cancellationToken);
        var clinicIds = clinics.Select(clinic => clinic.ID).ToList();
        if (query.ClinicId.HasValue && !clinicIds.Contains(query.ClinicId.Value))
            return new(false, Array.Empty<SystemAuditLog>(), Array.Empty<WhatsAppNotification>(), clinics);

        var auditQuery = _context.SystemAuditLogs.AsNoTracking()
            .Where(log => log.DealerId == query.DealerId
                          || (log.ClinicId.HasValue && clinicIds.Contains(log.ClinicId.Value)));
        if (!string.IsNullOrWhiteSpace(query.Level)) auditQuery = auditQuery.Where(log => log.Level == query.Level);
        if (!string.IsNullOrWhiteSpace(query.Source)) auditQuery = auditQuery.Where(log => log.Source == query.Source);
        if (!string.IsNullOrWhiteSpace(query.Category)) auditQuery = auditQuery.Where(log => log.Category == query.Category);
        if (query.From.HasValue)
            auditQuery = auditQuery.Where(log => log.CreatedAt >= DateTime.SpecifyKind(query.From.Value.Date, DateTimeKind.Utc));
        if (query.To.HasValue)
            auditQuery = auditQuery.Where(log => log.CreatedAt < DateTime.SpecifyKind(query.To.Value.Date.AddDays(1), DateTimeKind.Utc));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            auditQuery = auditQuery.Where(log =>
                log.Action.Contains(term)
                || log.Message.Contains(term)
                || (log.ActorUserName != null && log.ActorUserName.Contains(term))
                || (log.ErrorCode != null && log.ErrorCode.Contains(term))
                || (log.TraceId != null && log.TraceId.Contains(term)));
        }
        if (query.ClinicId.HasValue) auditQuery = auditQuery.Where(log => log.ClinicId == query.ClinicId);

        var notificationQuery = _context.WhatsAppNotifications.IgnoreQueryFilters().AsNoTracking()
            .Include(notification => notification.PetOwner)
            .Where(notification => clinicIds.Contains(notification.ClinicID)
                                   && (notification.LastError != null
                                       || notification.Status == WhatsAppNotificationStatuses.Failed
                                       || notification.Status == WhatsAppNotificationStatuses.NeedsReview));
        if (query.ClinicId.HasValue)
            notificationQuery = notificationQuery.Where(notification => notification.ClinicID == query.ClinicId);

        return new(
            true,
            await auditQuery.OrderByDescending(log => log.CreatedAt).Take(200).ToListAsync(cancellationToken),
            await notificationQuery.OrderByDescending(item => item.CreatedAt).Take(100).ToListAsync(cancellationToken),
            clinics);
    }
}
