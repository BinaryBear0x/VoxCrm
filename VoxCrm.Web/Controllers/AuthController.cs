using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Web.Controllers;

/// <summary>
/// Giriş / Çıkış işlemleri.
///
/// TEMEL KAVRAMLAR:
/// - Claim: Kullanıcının cookie'sine (çerezine) gömülen küçük veri parçaları.
///   Örnek: "Bu kullanıcının ClinicId'si = abc-123" → artık her istekte
///   veritabanına gitmeden bu bilgiyi okuyabiliyoruz.
/// - Role: Kullanıcının sistemdeki rolü ("Dealer" veya "Clinic").
///   Claim'den farkı: role, yetki kontrolü için özel olarak tasarlanmıştır.
/// </summary>
public class AuthController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser>   _userManager;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser>   userManager)
    {
        _signInManager = signInManager;
        _userManager   = userManager;
    }

    // GET: /Auth/Login
    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    { if (User.Identity?.IsAuthenticated == true && !User.HasClaim(c => c.Type == "ClinicId") && !User.HasClaim(c => c.Type == "DealerId"))
        {
            await _signInManager.SignOutAsync();
            ViewBag.ReturnUrl = returnUrl;
            return View(); 
        }

        // Zaten sağlıklı bir şekilde giriş yapmışsa direkt yönlendir, login sayfasını gösterme
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    // POST: /Auth/Login
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
    {
      
        var result = await _signInManager.PasswordSignInAsync(
            email, password,
            isPersistent: false,
            lockoutOnFailure: true); // <<< KRİTİK DEĞİŞİKLİK

        if (result.IsLockedOut)
        {
            ModelState.AddModelError("", "Hesabınız çok fazla hatalı deneme nedeniyle 10 dakika kilitlendi.");
            return View();
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError("", "E-posta veya şifre hatalı.");
            return View();
        }

       
        var user = await _userManager.FindByEmailAsync(email);
        if (user != null)
        {
            var additionalClaims = new List<Claim>();

            if (user.ClinicId.HasValue)
                additionalClaims.Add(new Claim("ClinicId", user.ClinicId.Value.ToString()));

            if (user.DealerId.HasValue)
                additionalClaims.Add(new Claim("DealerId", user.DealerId.Value.ToString()));

            // Claim'leri veritabanına ekle
            var existingClaims = await _userManager.GetClaimsAsync(user);
            var staleTenantClaims = existingClaims
                .Where(c => c.Type is "ClinicId" or "DealerId")
                .ToList();
            if (staleTenantClaims.Any())
                await _userManager.RemoveClaimsAsync(user, staleTenantClaims);

            if (additionalClaims.Any())
                await _userManager.AddClaimsAsync(user, additionalClaims);
            
            // Çerezi (cookie) anında yenile ki yeni claim'ler hemen aktif olsun
            await _signInManager.RefreshSignInAsync(user);
        }

        // ─── ROL BAZLI YÖNLENDİRME ──────────────────────────────────────────
        // Dealer → kendi paneline, Clinic kullanıcıları → klinik dashboarduna
        if (user != null && await _userManager.IsInRoleAsync(user, "Dealer"))
            return RedirectLocalOrDefault(returnUrl, "/Dealer");

        return RedirectLocalOrDefault(returnUrl, "/");
    }

    // POST: /Auth/Logout
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login");
    }

    // GET: /Auth/AccessDenied
    public IActionResult AccessDenied() => View();

    private IActionResult RedirectLocalOrDefault(string? returnUrl, string defaultUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);

        return LocalRedirect(defaultUrl);
    }
}
