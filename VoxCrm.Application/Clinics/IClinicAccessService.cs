namespace VoxCrm.Application.Clinics;

public enum ClinicAccessOperation
{
    UpdateProfile, TemporaryPassword, Unlock, Suspend, Enable, RevokeSessions, ResetMfa, RenewActivation, GrantCareApproval, RevokeCareApproval
}
public sealed record ClinicAccessUser(Guid Id, string FirstName, string LastName, string Email,
    bool LoginDisabled, bool LockedOut, bool HasPassword, bool TwoFactorEnabled, bool MustChangePassword, bool CanApproveCareCards);
public sealed record ClinicAccessPage(Guid ClinicId, string ClinicName, bool ClinicActive, IReadOnlyList<ClinicAccessUser> Users);
public sealed record ClinicAccessCommand(Guid DealerId, Guid ClinicId, Guid UserId, Guid ActorId,
    ClinicAccessOperation Operation, string DealerPassword, string Reason,
    string? FirstName = null, string? LastName = null, string? Email = null, string? NewPassword = null);
public sealed record ClinicAccessCreate(Guid DealerId, Guid ClinicId, Guid ActorId, string DealerPassword,
    string Reason, string FirstName, string LastName, string Email);
public sealed record ClinicAccessResult(Guid UserId, string Email, string? ActivationToken = null);
public sealed class ClinicAccessException(string message, bool notFound = false) : Exception(message)
{
    public bool NotFound { get; } = notFound;
}
public interface IClinicAccessService
{
    Task<ClinicAccessPage> GetAsync(Guid dealerId, Guid clinicId, Guid actorId, CancellationToken ct = default);
    Task<ClinicAccessResult> CreateAsync(ClinicAccessCreate command, CancellationToken ct = default);
    Task<ClinicAccessResult> ExecuteAsync(ClinicAccessCommand command, CancellationToken ct = default);
}
