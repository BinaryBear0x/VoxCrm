using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.PetOwners;

public sealed record PetOwnerDetails(PetOwner Owner, IReadOnlyList<Patient> AvailablePatients);
public sealed record PetOwnerCommandResult(bool Succeeded, PetOwner? Owner = null, string? Error = null, bool NotFound = false);

public interface IPetOwnerService
{
    Task<IReadOnlyList<PetOwner>> ListAsync(string? search, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PetOwner>> SearchAsync(string? search, CancellationToken cancellationToken = default);
    Task<PetOwnerDetails?> GetDetailsAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<PetOwner?> GetAsync(Guid id, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<PetOwnerCommandResult> CreateAsync(PetOwner owner, CancellationToken cancellationToken = default);
    Task<PetOwnerCommandResult> UpdateAsync(PetOwner owner, CancellationToken cancellationToken = default);
    Task<PetOwnerCommandResult> ArchiveAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<PetOwnerCommandResult> RestoreAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<PetOwnerCommandResult> AddPatientAsync(Guid ownerId, Guid patientId, CancellationToken cancellationToken = default);
    Task<PetOwnerCommandResult> RemovePatientAsync(Guid ownerId, Guid patientId, Guid actorUserId, CancellationToken cancellationToken = default);
}
