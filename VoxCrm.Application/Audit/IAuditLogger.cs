namespace VoxCrm.Application.Audit;

public interface IAuditLogger
{
    Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}
