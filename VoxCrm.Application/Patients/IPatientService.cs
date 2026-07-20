using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.Patients;

public sealed record PatientDetails(
    Patient Patient,
    IReadOnlyList<PetOwner> AvailableOwners,
    IReadOnlyList<Muayene> Examinations,
    IReadOnlyList<VaccinationRecord> Vaccinations,
    IReadOnlyList<Appointment> Appointments,
    IReadOnlyList<Debt> Debts);

public sealed record PatientCommandResult(bool Succeeded, Patient? Patient = null, string? Error = null, bool NotFound = false);

public interface IPatientService
{
    Task<IReadOnlyList<Patient>> ListAsync(string? search, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Patient>> SearchAsync(string? search, CancellationToken cancellationToken = default);
    Task<PatientDetails?> GetDetailsAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<Patient?> GetAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PetOwner>> GetActiveOwnersAsync(CancellationToken cancellationToken = default);
    Task<PatientCommandResult> CreateAsync(Patient patient, Guid? ownerId, CancellationToken cancellationToken = default);
    Task<PatientCommandResult> UpdateAsync(Patient patient, CancellationToken cancellationToken = default);
    Task<PatientCommandResult> ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<PatientCommandResult> RestoreAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<PatientCommandResult> AddOwnerAsync(Guid patientId, Guid ownerId, CancellationToken cancellationToken = default);
    Task<PatientCommandResult> RemoveOwnerAsync(Guid patientId, Guid ownerId, Guid actorUserId, CancellationToken cancellationToken = default);
}
