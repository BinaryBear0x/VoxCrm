using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
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

    public DealerController(
        SystemHealthService healthService,
        IClinicManagementService clinicManagementService,
        IDealerLogService dealerLogService)
    {
        _healthService = healthService;
        _clinicManagementService = clinicManagementService;
        _dealerLogService = dealerLogService;
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
                model.WhatsAppTimeZoneId),
            HttpContext.RequestAborted);
        if (!result.Succeeded)
            return NotFound();

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

    private bool TryGetDealerId(out Guid dealerId)
    {
        return Guid.TryParse(User.FindFirst("DealerId")?.Value, out dealerId);
    }
}
