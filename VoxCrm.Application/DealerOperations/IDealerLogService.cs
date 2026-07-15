using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.DealerOperations;

public sealed record DealerLogQuery(
    Guid DealerId,
    string? Level,
    string? Source,
    string? Category,
    string? Search,
    DateTime? From,
    DateTime? To,
    Guid? ClinicId);

public sealed record DealerLogResult(
    bool Succeeded,
    IReadOnlyList<SystemAuditLog> AuditLogs,
    IReadOnlyList<WhatsAppNotification> WhatsAppErrors,
    IReadOnlyList<Clinic> Clinics);

public interface IDealerLogService
{
    Task<DealerLogResult> GetAsync(DealerLogQuery query, CancellationToken cancellationToken = default);
}
