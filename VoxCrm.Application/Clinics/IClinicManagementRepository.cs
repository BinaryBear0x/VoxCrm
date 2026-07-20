using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.Clinics;

public interface IClinicManagementRepository
{
    Task<IReadOnlyList<Clinic>> ListOwnedAsync(Guid dealerId, CancellationToken cancellationToken);
    Task<Clinic?> FindOwnedAsync(Guid dealerId, Guid clinicId, CancellationToken cancellationToken);
    Task<ClinicProvisioningResult> CreateAsync(
        ClinicProvisioning provisioning,
        CancellationToken cancellationToken);
    Task<ClinicManagementResult> UpdateAsync(ClinicUpdate update, CancellationToken cancellationToken);
    Task<ClinicManagementResult> ChangeLifecycleAsync(
        ClinicLifecycleChange change,
        CancellationToken cancellationToken);
    Task<ClinicActivationStatus> GetActivationStatusAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken);
    Task<ClinicActivationResult> ActivateAsync(
        Guid userId,
        string token,
        string password,
        CancellationToken cancellationToken);
    Task<bool> IsUserScopeActiveAsync(
        Guid? clinicId,
        Guid? dealerId,
        CancellationToken cancellationToken);
}
