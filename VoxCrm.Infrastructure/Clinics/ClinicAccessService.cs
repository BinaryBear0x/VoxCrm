using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Application.Clinics;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.Clinics;

public sealed class ClinicAccessService(VoxCrmDbContext db, UserManager<ApplicationUser> users) : IClinicAccessService
{
    public async Task<ClinicAccessPage> GetAsync(Guid dealerId, Guid clinicId, Guid actorId, CancellationToken ct = default)
    {
        await AuthorizeActor(dealerId, actorId, ct);
        var clinic = await OwnedClinic(dealerId, clinicId, ct);
        var members = await db.Users.Where(x => x.ClinicId == clinicId).AsNoTracking().OrderBy(x => x.Email).ToListAsync(ct);
        var result = new List<ClinicAccessUser>();
        foreach (var u in members)
        {
            if (!await IsClinicUser(u)) continue;
            result.Add(new(u.Id, u.FirstName, u.LastName, u.Email ?? "", u.LoginDisabled,
                u.LockoutEnabled && u.LockoutEnd > DateTimeOffset.UtcNow, u.PasswordHash != null, u.TwoFactorEnabled, u.MustChangePassword, u.CanApproveCareCards));
        }
        return new(clinic.ID, clinic.Name, clinic.IsActive, result);
    }

    public async Task<ClinicAccessResult> CreateAsync(ClinicAccessCreate c, CancellationToken ct = default)
    {
        var actor = await AuthorizeActor(c.DealerId, c.ActorId, ct);
        await Confirm(actor, c.DealerPassword, c.Reason);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var clinic = await LockClinic(c.DealerId, c.ClinicId, ct);
            RequireActive(clinic);
            var email = Email(c.Email);
            await UniqueEmail(email, null, ct);
            var u = new ApplicationUser { UserName = email, Email = email, ClinicId = clinic.ID,
                FirstName = Text(c.FirstName, 100), LastName = Text(c.LastName, 100), LockoutEnabled = true };
            Check(await users.CreateAsync(u)); Check(await users.AddToRoleAsync(u, "Clinic"));
            var token = await users.GeneratePasswordResetTokenAsync(u);
            Audit(c.DealerId, c.ClinicId, actor, u, "Dealer.ClinicUserCreated", c.Reason);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return new(u.Id, email, token);
        }
        catch { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); throw; }
    }

    public async Task<ClinicAccessResult> ExecuteAsync(ClinicAccessCommand c, CancellationToken ct = default)
    {
        var actor = await AuthorizeActor(c.DealerId, c.ActorId, ct);
        await Confirm(actor, c.DealerPassword, c.Reason);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var clinic = await LockClinic(c.DealerId, c.ClinicId, ct);
            var u = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {c.UserId} AND \"ClinicId\" = {c.ClinicId} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (u == null || !await IsClinicUser(u)) throw Missing();
            string? token = null;
            switch (c.Operation)
            {
                case ClinicAccessOperation.UpdateProfile:
                    var email = Email(c.Email);
                    await UniqueEmail(email, u.Id, ct);
                    u.FirstName = Text(c.FirstName, 100); u.LastName = Text(c.LastName, 100);
                    if (!string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase))
                    {
                        // Identity validation (including uniqueness) and all updates share the transaction.
                        Check(await users.SetEmailAsync(u, email)); Check(await users.SetUserNameAsync(u, email));
                    }
                    Check(await users.UpdateAsync(u)); break;
                case ClinicAccessOperation.TemporaryPassword:
                    RequireActive(clinic);
                    if (u.LoginDisabled) throw Invalid("Askıya alınmış hesabı önce yeniden açın.");
                    if (string.IsNullOrEmpty(c.NewPassword) || c.NewPassword.Length < 12 || c.NewPassword.Length > 128)
                        throw Invalid("Geçici parola 12–128 karakter olmalıdır.");
                    var reset = await users.GeneratePasswordResetTokenAsync(u);
                    Check(await users.ResetPasswordAsync(u, reset, c.NewPassword));
                    u.MustChangePassword = true; Check(await users.UpdateAsync(u));
                    Check(await users.ResetAccessFailedCountAsync(u)); Check(await users.SetLockoutEndDateAsync(u, null)); break;
                case ClinicAccessOperation.Unlock:
                    RequireActive(clinic);
                    if (u.LoginDisabled) throw Invalid("Hesap askıya alınmış. Kilit açma yerine hesabı yeniden açın.");
                    Check(await users.ResetAccessFailedCountAsync(u)); Check(await users.SetLockoutEndDateAsync(u, null)); break;
                case ClinicAccessOperation.Suspend:
                    u.LoginDisabled = true; Check(await users.UpdateAsync(u));
                    Check(await users.SetLockoutEnabledAsync(u, true)); Check(await users.SetLockoutEndDateAsync(u, DateTimeOffset.MaxValue)); break;
                case ClinicAccessOperation.Enable:
                    RequireActive(clinic);
                    u.LoginDisabled = false; Check(await users.UpdateAsync(u));
                    Check(await users.ResetAccessFailedCountAsync(u)); Check(await users.SetLockoutEndDateAsync(u, null)); break;
                case ClinicAccessOperation.RevokeSessions: break;
                case ClinicAccessOperation.GrantCareApproval:
                    RequireActive(clinic); u.CanApproveCareCards = true; Check(await users.UpdateAsync(u)); break;
                case ClinicAccessOperation.RevokeCareApproval:
                    u.CanApproveCareCards = false; Check(await users.UpdateAsync(u)); break;
                case ClinicAccessOperation.ResetMfa:
                    Check(await users.SetTwoFactorEnabledAsync(u, false));
                    Check(await users.ResetAuthenticatorKeyAsync(u));
                    if (await users.GenerateNewTwoFactorRecoveryCodesAsync(u, 0) == null) throw Invalid("Kurtarma kodları sıfırlanamadı.");
                    break;
                case ClinicAccessOperation.RenewActivation:
                    RequireActive(clinic);
                    if (u.LoginDisabled || await users.HasPasswordAsync(u)) throw Invalid("Aktivasyon bağlantısı yalnız henüz parola belirlememiş açık hesaplar için oluşturulur.");
                    break;
                default: throw Invalid("Geçersiz hesap işlemi.");
            }
            // Revoke existing sessions, remembered devices and previously generated reset/activation tokens.
            Check(await users.UpdateSecurityStampAsync(u));
            if (c.Operation == ClinicAccessOperation.RenewActivation) token = await users.GeneratePasswordResetTokenAsync(u);
            Audit(c.DealerId, c.ClinicId, actor, u, "Dealer.ClinicUser." + c.Operation, c.Reason);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return new(u.Id, u.Email ?? "", token);
        }
        catch { await tx.RollbackAsync(ct); db.ChangeTracker.Clear(); throw; }
    }

    private async Task<ApplicationUser> AuthorizeActor(Guid dealerId, Guid actorId, CancellationToken ct)
    {
        var actor = await users.FindByIdAsync(actorId.ToString());
        if (actor == null || actor.DealerId != dealerId || actor.ClinicId.HasValue || actor.LoginDisabled
            || !await users.IsInRoleAsync(actor, "Dealer") || await users.IsLockedOutAsync(actor)
            || !await db.Dealers.AnyAsync(x => x.ID == dealerId && x.IsActive, ct)) throw Missing();
        return actor;
    }
    private async Task Confirm(ApplicationUser actor, string password, string reason)
    {
        Text(reason, 500);
        if (string.IsNullOrEmpty(password) || !await users.CheckPasswordAsync(actor, password)) throw Invalid("Bayi parolanız doğrulanamadı.");
    }
    private async Task UniqueEmail(string email, Guid? except, CancellationToken ct)
    {
        var normalized = users.NormalizeEmail(email);
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalized && (except == null || x.Id != except), ct))
            throw Invalid("Bu giriş e-postası başka bir hesapta kullanılıyor.");
    }
    private async Task<Clinic> OwnedClinic(Guid dealerId, Guid clinicId, CancellationToken ct) =>
        await db.Clinics.AsNoTracking().FirstOrDefaultAsync(x => x.ID == clinicId && x.DealerId == dealerId, ct) ?? throw Missing();
    private async Task<Clinic> LockClinic(Guid dealerId, Guid clinicId, CancellationToken ct) =>
        await db.Clinics.FromSqlInterpolated($"SELECT * FROM \"Clinics\" WHERE \"ID\" = {clinicId} AND \"DealerId\" = {dealerId} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw Missing();
    private async Task<bool> IsClinicUser(ApplicationUser u) => !u.DealerId.HasValue && await users.IsInRoleAsync(u, "Clinic") && !await users.IsInRoleAsync(u, "Dealer") && !await users.IsInRoleAsync(u, "SystemAdmin");
    private void Audit(Guid dealerId, Guid clinicId, ApplicationUser actor, ApplicationUser target, string action, string reason) =>
        db.SystemAuditLogs.Add(new SystemAuditLog { DealerId = dealerId, ClinicId = clinicId, ActorUserId = actor.Id,
            ActorUserName = actor.UserName, ActorRole = "Dealer", Category = "ClinicAccess", Action = action,
            Message = Text(reason, 500), EntityType = "ApplicationUser", EntityId = target.Id.ToString() });
    private static string Email(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 254 && new EmailAddressAttribute().IsValid(value.Trim()) ? value.Trim() : throw Invalid("Geçerli giriş e-postası girin.");
    private static string Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw Invalid($"Zorunlu alanları doldurun (en fazla {max} karakter).");
    private static void RequireActive(Clinic clinic) { if (!clinic.IsActive) throw Invalid("Önce kliniği aktifleştirin."); }
    private static void Check(IdentityResult result) { if (!result.Succeeded) throw Invalid(string.Join(" ", result.Errors.Select(x => x.Description))); }
    private static ClinicAccessException Missing() => new("Klinik veya kullanıcı bulunamadı.", true);
    private static ClinicAccessException Invalid(string message) => new(message);
}
