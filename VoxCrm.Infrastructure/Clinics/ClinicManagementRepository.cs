using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Clinics;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.Clinics;

public sealed class ClinicManagementRepository : IClinicManagementRepository
{
    private const string ClinicRole = "Clinic";
    private readonly VoxCrmDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    public ClinicManagementRepository(
        VoxCrmDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<IReadOnlyList<Clinic>> ListOwnedAsync(
        Guid dealerId,
        CancellationToken cancellationToken)
    {
        return await _context.Clinics
            .AsNoTracking()
            .Where(clinic => clinic.DealerId == dealerId)
            .OrderBy(clinic => clinic.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Clinic?> FindOwnedAsync(
        Guid dealerId,
        Guid clinicId,
        CancellationToken cancellationToken)
    {
        return _context.Clinics
            .AsNoTracking()
            .FirstOrDefaultAsync(
                clinic => clinic.ID == clinicId && clinic.DealerId == dealerId,
                cancellationToken);
    }

    public async Task<ClinicProvisioningResult> CreateAsync(
        ClinicProvisioning provisioning,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        if (!await _context.Dealers.AnyAsync(
                dealer => dealer.ID == provisioning.Clinic.DealerId && dealer.IsActive,
                cancellationToken))
        {
            return ClinicProvisioningResult.Failure("Aktif bayi hesabı bulunamadı.");
        }

        if (await _context.Clinics.AnyAsync(
                clinic => clinic.Slug == provisioning.Clinic.Slug,
                cancellationToken))
        {
            return ClinicProvisioningResult.Failure("Bu klinik adıyla oluşturulmuş bir kayıt zaten var.");
        }

        if (await _userManager.FindByEmailAsync(provisioning.InitialUser.Email!) != null)
        {
            return ClinicProvisioningResult.Failure("Bu e-posta adresiyle kayıtlı bir kullanıcı zaten var.");
        }

        var roleResult = await EnsureClinicRoleAsync();
        if (!roleResult.Succeeded)
            return ClinicProvisioningResult.Failure(ToErrors(roleResult));

        _context.Clinics.Add(provisioning.Clinic);
        await _context.SaveChangesAsync(cancellationToken);

        var createUserResult = await _userManager.CreateAsync(provisioning.InitialUser);
        if (!createUserResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ClinicProvisioningResult.Failure(ToErrors(createUserResult));
        }

        var addRoleResult = await _userManager.AddToRoleAsync(provisioning.InitialUser, ClinicRole);
        if (!addRoleResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ClinicProvisioningResult.Failure(ToErrors(addRoleResult));
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(provisioning.InitialUser);
        await transaction.CommitAsync(cancellationToken);

        return ClinicProvisioningResult.Success(new ProvisionedClinic(
            provisioning.Clinic.ID,
            provisioning.Clinic.Name,
            provisioning.InitialUser.Id,
            provisioning.InitialUser.Email!,
            token));
    }

    public async Task<ClinicManagementResult> UpdateAsync(
        ClinicUpdate update,
        CancellationToken cancellationToken)
    {
        var clinic = await _context.Clinics.FirstOrDefaultAsync(
            candidate => candidate.ID == update.ClinicId && candidate.DealerId == update.DealerId,
            cancellationToken);
        if (clinic == null)
            return ClinicManagementResult.Failure("Klinik bulunamadı.");

        clinic.Name = update.Name;
        clinic.phone = update.Phone;
        clinic.Email = update.Email;
        clinic.Address = update.Address;
        clinic.IsWhatsAppEnabled = update.IsWhatsAppEnabled;
        clinic.WhatsAppPhoneNumberId = update.WhatsAppPhoneNumberId;
        clinic.WhatsAppSendWindowEnabled = update.WhatsAppSendWindowEnabled;
        clinic.WhatsAppSendWindowStart = update.WhatsAppSendWindowStart;
        clinic.WhatsAppSendWindowEnd = update.WhatsAppSendWindowEnd;
        clinic.WhatsAppTimeZoneId = update.WhatsAppTimeZoneId;
        await _context.SaveChangesAsync(cancellationToken);

        return ClinicManagementResult.Success();
    }

    public async Task<ClinicManagementResult> ChangeLifecycleAsync(
        ClinicLifecycleChange change,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var clinic = await _context.Clinics.FirstOrDefaultAsync(
            candidate => candidate.ID == change.ClinicId && candidate.DealerId == change.DealerId,
            cancellationToken);
        if (clinic == null)
            return ClinicManagementResult.Failure("Klinik bulunamadı.");

        clinic.IsActive = change.IsActive;
        await _context.SaveChangesAsync(cancellationToken);

        var users = await _context.Users
            .Where(user => user.ClinicId == clinic.ID)
            .ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            var lifecycleResult = change.IsActive
                ? await ReactivateUserAsync(user)
                : await DeactivateUserAsync(user);
            if (!lifecycleResult.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ClinicManagementResult.Failure(ToErrors(lifecycleResult));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return ClinicManagementResult.Success();
    }

    public async Task<ClinicActivationStatus> GetActivationStatusAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (!await CanActivateAsync(user, token))
        {
            return new ClinicActivationStatus(
                false,
                null,
                new[] { "Aktivasyon bağlantısı geçersiz, süresi dolmuş veya daha önce kullanılmış." });
        }

        return new ClinicActivationStatus(true, user!.Email, Array.Empty<string>());
    }

    public async Task<ClinicActivationResult> ActivateAsync(
        Guid userId,
        string token,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (!await CanActivateAsync(user, token))
        {
            return new ClinicActivationResult(
                false,
                null,
                new[] { "Aktivasyon bağlantısı geçersiz, süresi dolmuş veya daha önce kullanılmış." });
        }

        var result = await _userManager.ResetPasswordAsync(user!, token, password);
        if (!result.Succeeded)
            return new ClinicActivationResult(false, user!.Email, ToErrors(result));

        user!.EmailConfirmed = true;
        var updateResult = await _userManager.UpdateAsync(user);
        return updateResult.Succeeded
            ? new ClinicActivationResult(true, user.Email, Array.Empty<string>())
            : new ClinicActivationResult(false, user.Email, ToErrors(updateResult));
    }

    public Task<bool> IsUserScopeActiveAsync(
        Guid? clinicId,
        Guid? dealerId,
        CancellationToken cancellationToken)
    {
        if (clinicId.HasValue == dealerId.HasValue)
            return Task.FromResult(false);

        return clinicId.HasValue
            ? _context.Clinics.AnyAsync(
                clinic => clinic.ID == clinicId.Value
                          && clinic.IsActive
                          && clinic.Dealer.IsActive,
                cancellationToken)
            : _context.Dealers.AnyAsync(
                dealer => dealer.ID == dealerId!.Value && dealer.IsActive,
                cancellationToken);
    }

    private async Task<bool> CanActivateAsync(ApplicationUser? user, string token)
    {
        if (user == null
            || user.ClinicId == null
            || user.PasswordHash != null
            || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var clinicIsActive = await _context.Clinics
            .AsNoTracking()
            .AnyAsync(clinic => clinic.ID == user.ClinicId && clinic.IsActive);
        if (!clinicIsActive)
            return false;

        return await _userManager.VerifyUserTokenAsync(
            user,
            _userManager.Options.Tokens.PasswordResetTokenProvider,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose,
            token);
    }

    private async Task<IdentityResult> EnsureClinicRoleAsync()
    {
        if (await _roleManager.RoleExistsAsync(ClinicRole))
            return IdentityResult.Success;

        return await _roleManager.CreateAsync(new IdentityRole<Guid>(ClinicRole));
    }

    private async Task<IdentityResult> DeactivateUserAsync(ApplicationUser user)
    {
        var lockoutEnabledResult = await _userManager.SetLockoutEnabledAsync(user, true);
        if (!lockoutEnabledResult.Succeeded)
            return lockoutEnabledResult;

        var lockoutResult = await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        if (!lockoutResult.Succeeded)
            return lockoutResult;

        return await _userManager.UpdateSecurityStampAsync(user);
    }

    private async Task<IdentityResult> ReactivateUserAsync(ApplicationUser user)
    {
        var unlockResult = await _userManager.SetLockoutEndDateAsync(user, null);
        if (!unlockResult.Succeeded)
            return unlockResult;

        var resetResult = await _userManager.ResetAccessFailedCountAsync(user);
        if (!resetResult.Succeeded)
            return resetResult;

        return await _userManager.UpdateSecurityStampAsync(user);
    }

    private static string[] ToErrors(IdentityResult result) =>
        result.Errors.Select(error => error.Description).ToArray();
}
