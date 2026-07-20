using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.Jobs;

public sealed class DataRetentionJob(VoxCrmDbContext context, IConfiguration configuration)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var messageDays = configuration.GetValue("DataRetention:WhatsAppMessageDays", 30);
        var auditDays = configuration.GetValue("DataRetention:AuditDays", 365);
        if (messageDays is < 1 or > 3650 || auditDays is < 30 or > 3650)
            throw new InvalidOperationException("Data retention values are outside the allowed range.");

        var messageCutoff = DateTime.UtcNow.AddDays(-messageDays);
        var auditCutoff = DateTime.UtcNow.AddDays(-auditDays);

        await context.WhatsAppInboundMessages.IgnoreQueryFilters()
            .Where(message => message.ReceivedAt < messageCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        await context.WhatsAppNotifications.IgnoreQueryFilters()
            .Where(message => message.CreatedAt < messageCutoff)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.PhoneNumber, "[silindi]")
                .SetProperty(message => message.MessageContent, "[30 günlük saklama süresi doldu]"), cancellationToken);

        await context.SystemAuditLogs
            .Where(log => log.CreatedAt < auditCutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
