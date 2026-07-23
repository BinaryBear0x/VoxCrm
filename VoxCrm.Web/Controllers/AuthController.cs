using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.RateLimiting;
using VoxCrm.Application.Clinics;
using VoxCrm.Domain.Entities;
using VoxCrm.Web.Models;
using VoxCrm.Application.Audit;

namespace VoxCrm.Web.Controllers;

public class AuthController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser>   _userManager;
    private readonly IClinicManagementService _clinicManagementService;
    private readonly IAuditLogger _audit;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IClinicManagementService clinicManagementService,
        IAuditLogger audit)
    {
        _signInManager = signInManager;
        _userManager   = userManager;
        _clinicManagementService = clinicManagementService;
        _audit = audit;
    }

    [EnableRateLimiting("authentication")]
    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    { if (User.Identity?.IsAuthenticated == true && !User.IsInRole("SystemAdmin") && !User.HasClaim(c => c.Type == "ClinicId") && !User.HasClaim(c => c.Type == "DealerId"))
        {
            await _signInManager.SignOutAsync();
            ViewBag.ReturnUrl = returnUrl;
            return View(); 
        }

        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [EnableRateLimiting("authentication")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
    {
        var user = await _userManager.FindByEmailAsync(email);
        var isSystemAdmin = user != null && await _userManager.IsInRoleAsync(user, "SystemAdmin");
        if (user == null || (!isSystemAdmin && !await _clinicManagementService.IsUserScopeActiveAsync(
                user.ClinicId,
                user.DealerId,
                HttpContext.RequestAborted)))
        {
            await LogLoginFailureAsync(user, "Auth.LoginFailed", "Unknown or inactive account login rejected.");
            ModelState.AddModelError("", "E-posta veya şifre hatalı.");
            return View();
        }

        var result = await _signInManager.PasswordSignInAsync(
            email, password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            await LogLoginFailureAsync(user, "Auth.AccountLocked", "Account locked after repeated login failures.");
            ModelState.AddModelError("", "Hesabınız çok fazla hatalı deneme nedeniyle 10 dakika kilitlendi.");
            return View();
        }

        if (result.RequiresTwoFactor)
            return RedirectToAction(nameof(LoginWith2fa), new { returnUrl });

        if (!result.Succeeded)
        {
            await LogLoginFailureAsync(user, "Auth.LoginFailed", "Invalid password login rejected.");
            ModelState.AddModelError("", "E-posta veya şifre hatalı.");
            return View();
        }

        if (user.MustChangePassword)
            return RedirectToAction(nameof(ChangePassword));

        if ((isSystemAdmin || await _userManager.IsInRoleAsync(user, "Dealer")) && !user.TwoFactorEnabled)
            return RedirectToAction(nameof(SetupMfa));

       
        if (user != null)
        {
            var additionalClaims = new List<Claim>();

            if (user.ClinicId.HasValue)
                additionalClaims.Add(new Claim("ClinicId", user.ClinicId.Value.ToString()));

            if (user.DealerId.HasValue)
                additionalClaims.Add(new Claim("DealerId", user.DealerId.Value.ToString()));

            var existingClaims = await _userManager.GetClaimsAsync(user);
            var staleTenantClaims = existingClaims
                .Where(c => c.Type is "ClinicId" or "DealerId")
                .ToList();
            if (staleTenantClaims.Any())
                await _userManager.RemoveClaimsAsync(user, staleTenantClaims);

            if (additionalClaims.Any())
                await _userManager.AddClaimsAsync(user, additionalClaims);
            
            await _signInManager.RefreshSignInAsync(user);
        }

        if (isSystemAdmin)
            return RedirectLocalOrDefault(returnUrl, "/hangfire");

        if (user != null && await _userManager.IsInRoleAsync(user, "Dealer"))
            return RedirectLocalOrDefault(returnUrl, "/Dealer");

        return RedirectLocalOrDefault(returnUrl, "/");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login");
    }

    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpGet]
    public IActionResult LoginWith2fa(string? returnUrl = null) => View(new TwoFactorLoginViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginWith2fa(TwoFactorLoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user == null) return RedirectToAction(nameof(Login));
        var code = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(code, false, false);
        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Hesap geçici olarak kilitlendi.");
            return View(model);
        }
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Doğrulama kodu geçersiz.");
            return View(model);
        }
        if (user.MustChangePassword) return RedirectToAction(nameof(ChangePassword));
        return RedirectLocalOrDefault(model.ReturnUrl, "/");
    }

    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpGet]
    public IActionResult LoginWithRecoveryCode(string? returnUrl = null) =>
        View(new RecoveryCodeLoginViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginWithRecoveryCode(RecoveryCodeLoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(model.RecoveryCode.Replace(" ", string.Empty));
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Kurtarma kodu geçersiz veya daha önce kullanılmış.");
            return View(model);
        }
        return RedirectLocalOrDefault(model.ReturnUrl, "/");
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> SetupMfa()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (user.TwoFactorEnabled) return RedirectToAction("Index", "Home");
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
        }
        return View(BuildMfaModel(user, key!));
    }

    [Authorize]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetupMfa(MfaSetupViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var key = await _userManager.GetAuthenticatorKeyAsync(user) ?? string.Empty;
        if (!ModelState.IsValid) return View(BuildMfaModel(user, key));
        var code = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        if (!await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code))
        {
            ModelState.AddModelError(nameof(model.Code), "Doğrulama kodu geçersiz.");
            return View(BuildMfaModel(user, key));
        }
        await _userManager.SetTwoFactorEnabledAsync(user, true);
        var codes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray() ?? [];
        await _audit.LogAsync(new AuditLogEntry
        {
            Source = AuditLogSources.Web, Action = "Auth.MfaEnabled", Message = "Privileged account MFA enabled.",
            ActorUserId = user.Id, ActorUserName = user.Email, DealerId = user.DealerId, ClinicId = user.ClinicId
        });
        return View(new MfaSetupViewModel { IsCompleted = true, RecoveryCodes = codes });
    }

    [Authorize(Roles = "SystemAdmin,Dealer")]
    [HttpGet]
    public IActionResult ResetMfa() => View(new ResetMfaViewModel());

    [Authorize(Roles = "SystemAdmin,Dealer")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetMfa(ResetMfaViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!ModelState.IsValid || !await _userManager.CheckPasswordAsync(user, model.CurrentPassword))
        {
            ModelState.AddModelError(string.Empty, "Parola doğrulanamadı.");
            return View(model);
        }

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);
        await _audit.LogAsync(new AuditLogEntry
        {
            Source = AuditLogSources.Web,
            Category = AuditLogCategories.Security,
            Action = "Auth.MfaReset",
            Message = "Privileged account MFA reset after password verification.",
            ActorUserId = user.Id,
            ActorUserName = user.Email,
            DealerId = user.DealerId,
            ClinicId = user.ClinicId
        });
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [Authorize]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);
        await _signInManager.RefreshSignInAsync(user);
        return User.IsInRole("SystemAdmin") || User.IsInRole("Dealer")
            ? RedirectToAction(nameof(SetupMfa))
            : RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpGet]
    public async Task<IActionResult> Activate(Guid userId, string? token)
    {
        if (!TryDecodeToken(token, out var decodedToken))
            return View(new ActivateClinicUserViewModel());

        var status = await _clinicManagementService.GetActivationStatusAsync(
            userId,
            decodedToken,
            HttpContext.RequestAborted);
        var model = new ActivateClinicUserViewModel
        {
            UserId = userId,
            Token = token!,
            Email = status.Email,
        };
        if (!status.IsValid)
        {
            foreach (var error in status.Errors)
                ModelState.AddModelError(string.Empty, error);
        }

        return View(model);
    }

    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(ActivateClinicUserViewModel model)
    {
        if (!TryDecodeToken(model.Token, out var decodedToken))
            ModelState.AddModelError(string.Empty, "Aktivasyon bağlantısı geçersiz.");

        if (!ModelState.IsValid)
            return View(model);

        var result = await _clinicManagementService.ActivateAsync(
            model.UserId,
            decodedToken,
            model.Password,
            HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View(model);
        }

        ModelState.Clear();
        return View(new ActivateClinicUserViewModel
        {
            Email = result.Email,
            IsCompleted = true,
        });
    }

    public IActionResult AccessDenied() => View();

    private Task LogLoginFailureAsync(ApplicationUser? user, string action, string message) =>
        _audit.LogAsync(new AuditLogEntry
        {
            Source = AuditLogSources.Web,
            Category = AuditLogCategories.Security,
            Level = AuditLogLevels.Warning,
            Outcome = AuditLogOutcomes.Denied,
            Action = action,
            Message = message,
            ActorUserId = user?.Id,
            ClinicId = user?.ClinicId,
            DealerId = user?.DealerId,
            HttpMethod = Request.Method,
            Path = Request.Path,
            StatusCode = StatusCodes.Status401Unauthorized,
            TraceId = HttpContext.TraceIdentifier
        }, HttpContext.RequestAborted);

    private IActionResult RedirectLocalOrDefault(string? returnUrl, string defaultUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return LocalRedirect(defaultUrl);
    }

    private static bool TryDecodeToken(string? encodedToken, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(encodedToken))
            return false;

        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
            return !string.IsNullOrWhiteSpace(token);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static MfaSetupViewModel BuildMfaModel(ApplicationUser user, string key)
    {
        var email = Uri.EscapeDataString(user.Email ?? user.UserName ?? "VoxCrm");
        var issuer = Uri.EscapeDataString("VoxCrm");
        return new MfaSetupViewModel
        {
            SharedKey = key,
            AuthenticatorUri = $"otpauth://totp/{issuer}:{email}?secret={key}&issuer={issuer}&digits=6"
        };
    }
}
