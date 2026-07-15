using System.Globalization;
using System.Text;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Application.Clinics;

public interface IClinicManagementService
{
    Task<IReadOnlyList<Clinic>> ListOwnedAsync(Guid dealerId, CancellationToken cancellationToken = default);
    Task<Clinic?> FindOwnedAsync(
        Guid dealerId,
        Guid clinicId,
        CancellationToken cancellationToken = default);
    Task<ClinicProvisioningResult> CreateAsync(
        CreateClinicCommand command,
        CancellationToken cancellationToken = default);
    Task<ClinicManagementResult> UpdateAsync(
        UpdateClinicCommand command,
        CancellationToken cancellationToken = default);
    Task<ClinicManagementResult> DeactivateAsync(
        Guid dealerId,
        Guid clinicId,
        CancellationToken cancellationToken = default);
    Task<ClinicManagementResult> ReactivateAsync(
        Guid dealerId,
        Guid clinicId,
        CancellationToken cancellationToken = default);
    Task<ClinicActivationStatus> GetActivationStatusAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default);
    Task<ClinicActivationResult> ActivateAsync(
        Guid userId,
        string token,
        string password,
        CancellationToken cancellationToken = default);
    Task<bool> IsUserScopeActiveAsync(
        Guid? clinicId,
        Guid? dealerId,
        CancellationToken cancellationToken = default);
}

public sealed class ClinicManagementService : IClinicManagementService
{
    private readonly IClinicManagementRepository _repository;

    public ClinicManagementService(IClinicManagementRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<Clinic>> ListOwnedAsync(
        Guid dealerId,
        CancellationToken cancellationToken = default) =>
        _repository.ListOwnedAsync(RequireDealerId(dealerId), cancellationToken);

    public Task<Clinic?> FindOwnedAsync(
        Guid dealerId,
        Guid clinicId,
        CancellationToken cancellationToken = default) =>
        _repository.FindOwnedAsync(RequireDealerId(dealerId), RequireClinicId(clinicId), cancellationToken);

    public Task<ClinicProvisioningResult> CreateAsync(
        CreateClinicCommand command,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateCreate(command);
        if (errors.Count > 0)
            return Task.FromResult(ClinicProvisioningResult.Failure(errors.ToArray()));

        var clinic = new Clinic
        {
            DealerId = command.DealerId,
            Name = command.Name.Trim(),
            Slug = CreateSlug(command.Name),
            phone = Normalize(command.Phone),
            Email = Normalize(command.Email),
            Address = Normalize(command.Address),
            IsWhatsAppEnabled = command.IsWhatsAppEnabled,
        };
        var userEmail = command.InitialUserEmail.Trim();
        var user = new ApplicationUser
        {
            UserName = userEmail,
            Email = userEmail,
            FirstName = command.InitialUserFirstName.Trim(),
            LastName = command.InitialUserLastName.Trim(),
            ClinicId = clinic.ID,
            EmailConfirmed = false,
            LockoutEnabled = true,
        };

        return _repository.CreateAsync(new ClinicProvisioning(clinic, user), cancellationToken);
    }

    public Task<ClinicManagementResult> UpdateAsync(
        UpdateClinicCommand command,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateUpdate(command);
        if (errors.Count > 0)
            return Task.FromResult(ClinicManagementResult.Failure(errors.ToArray()));

        return _repository.UpdateAsync(
            new ClinicUpdate(
                command.DealerId,
                command.ClinicId,
                command.Name.Trim(),
                Normalize(command.Phone),
                Normalize(command.Email),
                Normalize(command.Address),
                command.IsWhatsAppEnabled,
                NormalizeNullable(command.WhatsAppPhoneNumberId),
                command.WhatsAppSendWindowEnabled,
                command.WhatsAppSendWindowStart,
                command.WhatsAppSendWindowEnd,
                string.IsNullOrWhiteSpace(command.WhatsAppTimeZoneId)
                    ? "Europe/Istanbul"
                    : command.WhatsAppTimeZoneId.Trim()),
            cancellationToken);
    }

    public Task<ClinicManagementResult> DeactivateAsync(
        Guid dealerId,
        Guid clinicId,
        CancellationToken cancellationToken = default) =>
        ChangeLifecycleAsync(dealerId, clinicId, isActive: false, cancellationToken);

    public Task<ClinicManagementResult> ReactivateAsync(
        Guid dealerId,
        Guid clinicId,
        CancellationToken cancellationToken = default) =>
        ChangeLifecycleAsync(dealerId, clinicId, isActive: true, cancellationToken);

    public Task<ClinicActivationStatus> GetActivationStatusAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default) =>
        _repository.GetActivationStatusAsync(userId, token, cancellationToken);

    public Task<ClinicActivationResult> ActivateAsync(
        Guid userId,
        string token,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(new ClinicActivationResult(
                false,
                null,
                new[] { "Aktivasyon bağlantısı geçersiz." }));
        }

        return _repository.ActivateAsync(userId, token, password, cancellationToken);
    }

    public Task<bool> IsUserScopeActiveAsync(
        Guid? clinicId,
        Guid? dealerId,
        CancellationToken cancellationToken = default) =>
        _repository.IsUserScopeActiveAsync(clinicId, dealerId, cancellationToken);

    private Task<ClinicManagementResult> ChangeLifecycleAsync(
        Guid dealerId,
        Guid clinicId,
        bool isActive,
        CancellationToken cancellationToken) =>
        _repository.ChangeLifecycleAsync(
            new ClinicLifecycleChange(
                RequireDealerId(dealerId),
                RequireClinicId(clinicId),
                isActive),
            cancellationToken);

    private static List<string> ValidateCreate(CreateClinicCommand command)
    {
        var errors = new List<string>();
        if (command.DealerId == Guid.Empty) errors.Add("Bayi bilgisi geçersiz.");
        if (string.IsNullOrWhiteSpace(command.Name)) errors.Add("Klinik adı zorunludur.");
        if (string.IsNullOrWhiteSpace(command.InitialUserFirstName)) errors.Add("İlk kullanıcı adı zorunludur.");
        if (string.IsNullOrWhiteSpace(command.InitialUserLastName)) errors.Add("İlk kullanıcı soyadı zorunludur.");
        if (string.IsNullOrWhiteSpace(command.InitialUserEmail)) errors.Add("İlk kullanıcı e-postası zorunludur.");
        return errors;
    }

    private static List<string> ValidateUpdate(UpdateClinicCommand command)
    {
        var errors = new List<string>();
        if (command.DealerId == Guid.Empty) errors.Add("Bayi bilgisi geçersiz.");
        if (command.ClinicId == Guid.Empty) errors.Add("Klinik bilgisi geçersiz.");
        if (string.IsNullOrWhiteSpace(command.Name)) errors.Add("Klinik adı zorunludur.");
        return errors;
    }

    private static Guid RequireDealerId(Guid dealerId) =>
        dealerId == Guid.Empty ? throw new ArgumentException("Dealer ID is required.", nameof(dealerId)) : dealerId;

    private static Guid RequireClinicId(Guid clinicId) =>
        clinicId == Guid.Empty ? throw new ArgumentException("Clinic ID is required.", nameof(clinicId)) : clinicId;

    private static string CreateSlug(string value)
    {
        var normalized = value.Trim().ToLowerInvariant()
            .Replace('ı', 'i')
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var pendingSeparator = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && builder.Length > 0)
                    builder.Append('-');
                builder.Append(character);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString();
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
