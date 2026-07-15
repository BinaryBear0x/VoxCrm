using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.Vaccinations;

public sealed record VaccinationChoices(IReadOnlyList<Patient> Patients, IReadOnlyList<VaccineType> VaccineTypes);
public sealed record VaccinationCommandResult(bool Succeeded, VaccinationRecord? Record = null, string? Error = null, bool NotFound = false);

public interface IVaccinationService
{
    Task<IReadOnlyList<VaccinationRecord>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<VaccinationRecord?> GetAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<VaccinationChoices> GetChoicesAsync(CancellationToken cancellationToken = default);
    Task<VaccinationCommandResult> CreateAsync(VaccinationRecord record, CancellationToken cancellationToken = default);
    Task<VaccinationCommandResult> UpdateAsync(VaccinationRecord record, CancellationToken cancellationToken = default);
    Task<VaccinationCommandResult> ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<VaccinationCommandResult> RestoreAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed record VaccineTypeCommandResult(bool Succeeded, VaccineType? VaccineType = null, string? Error = null, bool NotFound = false);

public interface IVaccineTypeService
{
    Task<IReadOnlyList<VaccineType>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<VaccineType?> GetAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<VaccineTypeCommandResult> CreateAsync(VaccineType vaccineType, CancellationToken cancellationToken = default);
    Task<VaccineTypeCommandResult> UpdateAsync(VaccineType vaccineType, CancellationToken cancellationToken = default);
    Task<VaccineTypeCommandResult> ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<VaccineTypeCommandResult> RestoreAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
}
