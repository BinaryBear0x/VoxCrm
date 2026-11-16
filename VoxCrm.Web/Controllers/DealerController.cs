using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using VoxCrm.Application.Clinics;
using VoxCrm.Application.DealerOperations;
using VoxCrm.Domain.Entities;
using VoxCrm.Web.Models;
using VoxCrm.Web.Services;

namespace VoxCrm.Web.Controllers;

[Authorize(Roles = "Dealer")]
public class DealerController : Controller
{
    private readonly SystemHealthService _healthService;
    private readonly IClinicManagementService _clinicManagementService;
    private readonly IDealerLogService _dealerLogService;
    private readonly IClinicAccessService _clinicAccess;

    public DealerController(
        SystemHealthService healthService,
        IClinicManagementService clinicManagementService,
        IDealerLogService dealerLogService, IClinicAccessService clinicAccess)
    {
        _healthService = healthService;
        _clinicManagementService = clinicManagementService;
        _dealerLogService = dealerLogService;
        _clinicAccess = clinicAccess;
    }

    [HttpGet]
    public async Task<IActionResult> Modules(Guid id,[FromServices] ClinicModules modules,CancellationToken ct)
    {
        if(!TryGetDealerId(out var dealer))return Forbid();
        var clinic=await modules.OwnedAsync(dealer,id,ct);return clinic==null?NotFound():View(clinic);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Modules(Guid id,long revision,bool quickVisit,bool followUp,bool whatsApp,[FromServices] ClinicModules modules,CancellationToken ct,bool workday=false)
    {
        if(!TryGetDealerId(out var dealer)||!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var actor))return Forbid();
        if(!ModelState.IsValid)return BadRequest("Geçerli modül seçimi ve sürüm gerekli.");
        try{await modules.SaveAsync(dealer,actor,id,revision,quickVisit,followUp,whatsApp,ct,workday);TempData["Success"]="Modüller güncellendi. Kayıtlar korundu.";}
        catch(KeyNotFoundException){return NotFound();}catch(InvalidOperationException e){TempData["Error"]=e.Message;}
        return RedirectToAction(nameof(Modules),new{id});
    }

    public async Task<IActionResult> Index()
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        var clinics = await _clinicManagementService.ListOwnedAsync(dealerId, HttpContext.RequestAborted);
        return View(clinics);
    }

    public IActionResult Create()
    {
        return TryGetDealerId(out _) ? View(new CreateClinicViewModel()) : Forbid();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateClinicViewModel model)
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        if (!ModelState.IsValid)
            return View(model);

        var result = await _clinicManagementService.CreateAsync(
            new CreateClinicCommand(
                dealerId,
                model.Name,
                model.Phone,
                model.Email,
                model.Address,
                model.IsWhatsAppEnabled,
                model.InitialUserFirstName,
                model.InitialUserLastName,
                model.InitialUserEmail),
            HttpContext.RequestAborted);
        if (!result.Succeeded || result.Provisioned == null)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View(model);
        }

        var encodedToken = WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes(result.Provisioned.ActivationToken));
        var activationUrl = Url.Action(
            "Activate",
            "Auth",
            new { userId = result.Provisioned.UserId, token = encodedToken },
            Request.Scheme);

        return View("Provisioned", new ProvisionedClinicViewModel
        {
            ClinicName = result.Provisioned.ClinicName,
            UserEmail = result.Provisioned.UserEmail,
            ActivationUrl = activationUrl ?? string.Empty,
        });
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        var clinic = await _clinicManagementService.FindOwnedAsync(
            dealerId,
            id,
            HttpContext.RequestAborted);
        if (clinic == null) return NotFound();
        return View(new EditClinicViewModel
        {
            ClinicId = clinic.ID,
            ModulesRevision = clinic.ModulesRevision,
            Name = clinic.Name,
            Phone = clinic.phone,
            Email = clinic.Email,
            Address = clinic.Address,
            IsWhatsAppEnabled = clinic.IsWhatsAppEnabled,
            WhatsAppPhoneNumberId = clinic.WhatsAppPhoneNumberId,
            WhatsAppSendWindowEnabled = clinic.WhatsAppSendWindowEnabled,
            WhatsAppSendWindowStart = clinic.WhatsAppSendWindowStart,
            WhatsAppSendWindowEnd = clinic.WhatsAppSendWindowEnd,
            WhatsAppTimeZoneId = clinic.WhatsAppTimeZoneId,
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditClinicViewModel model)
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();
        model.ClinicId = id;
        if (!ModelState.IsValid)
            return View(model);

        var result = await _clinicManagementService.UpdateAsync(
            new UpdateClinicCommand(
                dealerId,
                id,
                model.Name,
                model.Phone,
                model.Email,
                model.Address,
                model.IsWhatsAppEnabled,
                model.WhatsAppPhoneNumberId,
                model.WhatsAppSendWindowEnabled,
                model.WhatsAppSendWindowStart,
                model.WhatsAppSendWindowEnd,
                model.WhatsAppTimeZoneId, model.ModulesRevision),
            HttpContext.RequestAborted);
        if (!result.Succeeded){foreach(var error in result.Errors)ModelState.AddModelError(string.Empty,error);return View(model);}

        TempData["Success"] = "Klinik bilgileri güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    // Preserve the legacy endpoint while retaining soft-delete semantics.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        return await Deactivate(id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        var result = await _clinicManagementService.DeactivateAsync(
            dealerId,
            id,
            HttpContext.RequestAborted);
        if (!result.Succeeded) return NotFound();

        TempData["Success"] = "Klinik pasifleştirildi ve kullanıcı oturumları kapatıldı.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        var result = await _clinicManagementService.ReactivateAsync(
            dealerId,
            id,
            HttpContext.RequestAborted);
        if (!result.Succeeded) return NotFound();

        TempData["Success"] = "Klinik ve kullanıcı hesapları yeniden aktifleştirildi.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Logs(
        string? level,
        string? source,
        string? category,
        string? search,
        DateTime? from,
        DateTime? to,
        Guid? clinicId,
        CancellationToken cancellationToken)
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        var result = await _dealerLogService.GetAsync(
            new DealerLogQuery(dealerId, level, source, category, search, from, to, clinicId),
            cancellationToken);
        if (!result.Succeeded) return Forbid();

        var model = new DealerLogsViewModel
        {
            AuditLogs = result.AuditLogs,
            WhatsAppErrors = result.WhatsAppErrors,
            Clinics = result.Clinics,
            Level = level,
            Source = source,
            Category = category,
            Search = search,
            From = from,
            To = to,
            ClinicId = clinicId
        };

        ViewData["ActiveMenu"] = "dealer-logs";
        return View(model);
    }

    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        if (!TryGetDealerId(out var dealerId)) return Forbid();

        var model = await _healthService.BuildDealerHealthAsync(dealerId, cancellationToken);
        ViewData["ActiveMenu"] = "dealer-health";
        return View(model);
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Users(Guid id)
    {
        if (!TryAccessActor(out var dealerId, out var actorId)) return Forbid();
        try { return View(await _clinicAccess.GetAsync(dealerId, id, actorId, HttpContext.RequestAborted)); }
        catch (ClinicAccessException e) when (e.NotFound) { return NotFound(); }
    }

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UserAccess(Guid id, Guid userId)
    {
        if (!TryAccessActor(out var dealerId, out var actorId)) return Forbid();
        try
        {
            var page = await _clinicAccess.GetAsync(dealerId, id, actorId, HttpContext.RequestAborted);
            var user = page.Users.FirstOrDefault(x => x.Id == userId);
            return user == null ? NotFound() : View(new ClinicUserAccessViewModel { Clinic = page, User = user });
        }
        catch (ClinicAccessException e) when (e.NotFound) { return NotFound(); }
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("authentication")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CreateUser(Guid id, CreateClinicUserInput input)
    {
        if (!TryAccessActor(out var dealerId, out var actorId)) return Forbid();
        try
        {
            // Validate ownership even when input validation fails.
            await _clinicAccess.GetAsync(dealerId, id, actorId, HttpContext.RequestAborted);
            if (!ModelState.IsValid) { TempData["AccessError"] = "Ad, soyad, geçerli e-posta, gerekçe ve bayi parolanız gereklidir."; return RedirectToAction(nameof(Users), new { id }); }
            var result = await _clinicAccess.CreateAsync(new(dealerId, id, actorId, input.DealerPassword, input.Reason, input.FirstName, input.LastName, input.Email), HttpContext.RequestAborted);
            return Activation(id, result);
        }
        catch (ClinicAccessException e)
        {
            if (e.NotFound) return NotFound();
            TempData["AccessError"] = e.Message; return RedirectToAction(nameof(Users), new { id });
        }
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("authentication")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ManageUser(Guid id, Guid userId, ClinicUserActionInput input)
    {
        if (!TryAccessActor(out var dealerId, out var actorId)) return Forbid();
        try
        {
            var page = await _clinicAccess.GetAsync(dealerId, id, actorId, HttpContext.RequestAborted);
            if (!page.Users.Any(x => x.Id == userId)) return NotFound();
            if (!ModelState.IsValid) { TempData["AccessError"] = "İşlem alanlarını, gerekçeyi ve bayi parolanızı kontrol edin."; return RedirectToAction(nameof(UserAccess), new { id, userId }); }
            var result = await _clinicAccess.ExecuteAsync(new(dealerId, id, userId, actorId, input.Operation, input.DealerPassword, input.Reason, input.FirstName, input.LastName, input.Email, input.NewPassword), HttpContext.RequestAborted);
            if (result.ActivationToken != null) return Activation(id, result);
            TempData["Success"] = "Kullanıcı işlemi tamamlandı, oturumları kapatıldı ve işlem loglandı.";
            return RedirectToAction(nameof(UserAccess), new { id, userId });
        }
        catch (ClinicAccessException e)
        {
            if (e.NotFound) return NotFound();
            TempData["AccessError"] = e.Message; return RedirectToAction(nameof(UserAccess), new { id, userId });
        }
    }

    private IActionResult Activation(Guid clinicId, ClinicAccessResult result)
    {
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(result.ActivationToken!));
        var url = Url.Action("Activate", "Auth", new { userId = result.UserId, token }, Request.Scheme) ?? "";
        return View("UserActivation", new ClinicUserActivationViewModel(clinicId, result.Email, url));
    }

    private bool TryAccessActor(out Guid dealerId, out Guid actorId)
    {
        var actorValid = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out actorId);
        return TryGetDealerId(out dealerId) && actorValid;
    }

    private bool TryGetDealerId(out Guid dealerId)
    {
        return Guid.TryParse(User.FindFirst("DealerId")?.Value, out dealerId);
    }
}
