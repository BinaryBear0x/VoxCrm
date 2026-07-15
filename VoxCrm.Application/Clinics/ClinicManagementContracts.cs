using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.Clinics;

public sealed record CreateClinicCommand(
    Guid DealerId,
    string Name,
    string? Phone,
    string? Email,
    string? Address,
    bool IsWhatsAppEnabled,
    string InitialUserFirstName,
    string InitialUserLastName,
    string InitialUserEmail);

public sealed record UpdateClinicCommand(
    Guid DealerId,
    Guid ClinicId,
    string Name,
    string? Phone,
    string? Email,
    string? Address,
    bool IsWhatsAppEnabled,
    string? WhatsAppPhoneNumberId,
    bool WhatsAppSendWindowEnabled,
    TimeOnly WhatsAppSendWindowStart,
    TimeOnly WhatsAppSendWindowEnd,
    string? WhatsAppTimeZoneId);

public sealed record ProvisionedClinic(
    Guid ClinicId,
    string ClinicName,
    Guid UserId,
    string UserEmail,
    string ActivationToken);

public sealed record ClinicManagementResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static ClinicManagementResult Success() => new(true, Array.Empty<string>());
    public static ClinicManagementResult Failure(params string[] errors) => new(false, errors);
}

public sealed record ClinicProvisioningResult(
    bool Succeeded,
    ProvisionedClinic? Provisioned,
    IReadOnlyList<string> Errors)
{
    public static ClinicProvisioningResult Success(ProvisionedClinic provisioned) =>
        new(true, provisioned, Array.Empty<string>());

    public static ClinicProvisioningResult Failure(params string[] errors) =>
        new(false, null, errors);
}

public sealed record ClinicActivationStatus(
    bool IsValid,
    string? Email,
    IReadOnlyList<string> Errors);

public sealed record ClinicActivationResult(
    bool Succeeded,
    string? Email,
    IReadOnlyList<string> Errors);

public sealed record ClinicLifecycleChange(Guid DealerId, Guid ClinicId, bool IsActive);

public sealed record ClinicUpdate(
    Guid DealerId,
    Guid ClinicId,
    string Name,
    string Phone,
    string Email,
    string Address,
    bool IsWhatsAppEnabled,
    string? WhatsAppPhoneNumberId,
    bool WhatsAppSendWindowEnabled,
    TimeOnly WhatsAppSendWindowStart,
    TimeOnly WhatsAppSendWindowEnd,
    string WhatsAppTimeZoneId);

public sealed record ClinicProvisioning(
    Clinic Clinic,
    ApplicationUser InitialUser);
