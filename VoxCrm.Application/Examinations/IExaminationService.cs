using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.Examinations;

public sealed record ExaminationResult(bool Succeeded, Muayene? Examination = null, string? Error = null, bool NotFound = false);

public interface IExaminationService
{
    Task<IReadOnlyList<Muayene>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Patient>> GetPatientOptionsAsync(CancellationToken cancellationToken = default);
    Task<Muayene?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ExaminationResult> CreateAsync(Muayene examination, CancellationToken cancellationToken = default);
    Task<ExaminationResult> UpdateAsync(Muayene examination, CancellationToken cancellationToken = default);
    Task<ExaminationResult> ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ExaminationResult> RestoreAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
}
